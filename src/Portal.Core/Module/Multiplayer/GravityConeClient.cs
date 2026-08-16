using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;

namespace Portal.Core.Module.Multiplayer;

public sealed class GravityConeClient : IAsyncDisposable
{
    private readonly ConcurrentDictionary<int, PendingRequest> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _nextId;
    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };

    public async ValueTask DisposeAsync()
    {
        var process = _process;
        if (process is null) return;
        if (!process.HasExited)
            try
            {
                await RequestAsync("system.shutdown", timeout: TimeSpan.FromSeconds(3));
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(4));
            }
            catch
            {
                if (!process.HasExited) process.Kill(true);
            }

        _process = null;
        process.Dispose();
        _writeLock.Dispose();
    }

    public event EventHandler<GravityConeEvent>? EventReceived;
    public event EventHandler<string>? ProcessError;

    public async Task StartAsync(GravityConeInstallation installation, CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false }) return;

        var peers = await GetPeersAsync(cancellationToken);

        var startInfo = new ProcessStartInfo(installation.CliPath)
        {
            WorkingDirectory = Path.GetDirectoryName(installation.CliPath)!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--easytier-dir");
        startInfo.ArgumentList.Add(installation.EasyTierDirectory);
        startInfo.ArgumentList.Add("--vendor");
        startInfo.ArgumentList.Add("Portal");
        startInfo.ArgumentList.Add("--motd");
        startInfo.ArgumentList.Add("Portal 联机房间");
        foreach (var peer in peers)
        {
            startInfo.ArgumentList.Add("--peers");
            startInfo.ArgumentList.Add(peer);
        }


        _process = await Task.Run(() =>
        {
            var p = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!p.Start()) throw new InvalidOperationException("无法启动 GravityCone CLI。");
            return p;
        });
        _process.Exited += (_, _) => FailAllPending("GravityCone CLI 已退出。");
        ReadOutputAsync(_process).Forget($"读取 GravityCone 标准输出，进程：{_process.Id}");
        ReadErrorAsync(_process).Forget($"读取 GravityCone 错误输出，进程：{_process.Id}");

        using var ready = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ready.Token);
        while (!linked.IsCancellationRequested)
            try
            {
                var response = await RequestAsync("system.ping", null, null, TimeSpan.FromSeconds(2), linked.Token);
                if (response.Status == "success") return;
            }
            catch when (!linked.IsCancellationRequested)
            {
                await Task.Delay(150, linked.Token);
            }

        throw new TimeoutException("GravityCone CLI 启动超时。");
    }

    public async Task RestartAsync(GravityConeInstallation installation, CancellationToken cancellationToken)
    {
        var process = _process;
        if (process is { HasExited: false })
            try
            {
                await RequestAsync("system.shutdown", timeout: TimeSpan.FromSeconds(3),
                    cancellationToken: cancellationToken);
                await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(4), cancellationToken);
            }
            catch
            {
                if (!process.HasExited) process.Kill(true);
            }

        _process = null;
        process?.Dispose();
        FailAllPending("GravityCone CLI 正在重启。");
        await StartAsync(installation, cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> GetPeersAsync(CancellationToken cancellationToken)
    {
        var peers = new List<string>();

        try
        {
            if (GravityConeNodeClient.IsUptimeConfigured)
                peers.AddRange(await GravityConeNodeClient.Instance.FetchPeerUrlsAsync(cancellationToken));
            else if (await GravityConeNodeClient.Instance.TryReadCacheAsync(cancellationToken) is { } cachedUptime)
                peers.AddRange(cachedUptime);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
                                   ex is HttpRequestException or JsonException or InvalidOperationException
                                       or IOException)
        {
            Logger.Warning($"获取 1TMC 联机节点失败，将尝试读取本地缓存。{Environment.NewLine}{ex}");
            if (await GravityConeNodeClient.Instance.TryReadCacheAsync(cancellationToken) is { } cachedUptime)
                peers.AddRange(cachedUptime);
        }

        try
        {
            if (await GravityConeRelayClient.Instance.TryReadCacheAsync(cancellationToken) is { } cachedRelays)
                peers.AddRange(cachedRelays);
            else
                peers.AddRange(await GravityConeRelayClient.Instance.FetchRelaysAsync(cancellationToken));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
                                   ex is HttpRequestException or JsonException or InvalidOperationException
                                       or IOException)
        {
            Logger.Warning($"获取 Portal 中继节点失败，将尝试读取本地缓存。{Environment.NewLine}{ex}");
            if (await GravityConeRelayClient.Instance.TryReadCacheAsync(cancellationToken) is { } cachedRelays)
                peers.AddRange(cachedRelays);
        }

        var merged = peers.Distinct(StringComparer.Ordinal).ToList();
        if (merged.Count == 0)
            throw new InvalidOperationException("无法获取联机节点列表，请检查网络后重试。");

        return merged;
    }

    public async Task<GravityConeResponse> RequestAsync(string method, object? parameters = null,
        IProgress<GravityConeProgress>? progress = null, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (_process is not { HasExited: false } process)
            throw new InvalidOperationException("GravityCone CLI 尚未启动。");

        var id = Interlocked.Increment(ref _nextId);
        var completion =
            new TaskCompletionSource<GravityConeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, new PendingRequest(completion, progress)))
            throw new InvalidOperationException("无法创建 CLI 请求。");

        try
        {
            var payload = JsonSerializer.Serialize(new { id, method, @params = parameters ?? new { } });
            Trace($"[GravityCone ->] {payload}");
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await process.StandardInput.WriteLineAsync(payload.AsMemory(), cancellationToken);
                await process.StandardInput.FlushAsync(cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }

            using var timeoutSource = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(45));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            using var registration = linked.Token.Register(() => completion.TrySetCanceled(linked.Token));
            return await completion.Task;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"GravityCone 请求 {method} 超时。");
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task ReadOutputAsync(Process process)
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                Trace($"[GravityCone <-] {line}");
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("event", out var eventName))
                {
                    EventReceived?.Invoke(this, new GravityConeEvent(eventName.GetString() ?? string.Empty,
                        root.TryGetProperty("data", out var eventData) ? eventData.Clone() : default));
                    continue;
                }

                if (!root.TryGetProperty("id", out var idElement) ||
                    !_pending.TryGetValue(idElement.GetInt32(), out var pending)) continue;

                var response = JsonSerializer.Deserialize<GravityConeResponse>(line)!;
                if (response.Status == "progress")
                {
                    pending.Progress?.Report(new GravityConeProgress(response.Data));
                    continue;
                }

                if (response.Status == "error")
                {
                    pending.Completion.TrySetException(new GravityConeException(
                        response.Error?.Code ?? "UNKNOWN", response.Error?.Message ?? "联机操作失败。"));
                    continue;
                }

                pending.Completion.TrySetResult(response);
            }
        }
        catch (Exception ex) when (process.HasExited)
        {
            FailAllPending(ex.Message);
        }
        catch (Exception ex)
        {
            ProcessError?.Invoke(this, ex.Message);
            FailAllPending(ex.Message);
        }
    }

    private async Task ReadErrorAsync(Process process)
    {
        while (await process.StandardError.ReadLineAsync() is { } line)
        {
            Trace($"[GravityCone stderr] {line}");
            ProcessError?.Invoke(this, line);
        }
    }

    private void FailAllPending(string message)
    {
        foreach (var request in _pending.Values)
            request.Completion.TrySetException(new InvalidOperationException(message));
        _pending.Clear();
    }

    public async Task StopAsync()
    {
        var process = _process;
        if (process is null) return;
        if (!process.HasExited)
            try
            {
                await RequestAsync("system.shutdown", timeout: TimeSpan.FromSeconds(3));
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(4));
            }
            catch
            {
                if (!process.HasExited) process.Kill(true);
            }

        _process = null;
        process.Dispose();
        FailAllPending("GravityCone CLI 已停止。");
    }

    private static void Trace(string message)
    {
        Console.WriteLine(message);
        Logger.Debug(message);
    }

    private sealed record PendingRequest(
        TaskCompletionSource<GravityConeResponse> Completion,
        IProgress<GravityConeProgress>? Progress);
}

public sealed class GravityConeResponse
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("data")] public JsonElement Data { get; init; }
    [JsonPropertyName("error")] public GravityConeError? Error { get; init; }
}

public sealed class GravityConeError
{
    [JsonPropertyName("code")] public string Code { get; init; } = string.Empty;
    [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
}

public sealed record GravityConeEvent(string Name, JsonElement Data);

public sealed record GravityConeProgress(JsonElement Data);

public sealed class GravityConeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
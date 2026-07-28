using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Module.Multiplayer;

public sealed class GravityConeClient : IAsyncDisposable
{
    private static readonly string[] BuiltInPeers =
    [
        "https://etnode.zkitefly.eu.org/node1",
        "wss://center.node.1tmc.top",
        "tcp://public.easytier.top:11010",
        "tcp://public2.easytier.cn:54321"
    ];

    private readonly ConcurrentDictionary<int, PendingRequest> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Process? _process;
    private int _nextId;

    public event EventHandler<GravityConeEvent>? EventReceived;
    public event EventHandler<string>? ProcessError;

    public async Task StartAsync(GravityConeInstallation installation, CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false }) return;

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
        foreach (var peer in BuiltInPeers)
        {
            startInfo.ArgumentList.Add("--peers");
            startInfo.ArgumentList.Add(peer);
        }

        // Process.Start() is synchronous and may take hundreds of ms;
        // run it on a background thread to avoid blocking the UI.
        _process = await Task.Run(() =>
        {
            var p = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!p.Start()) throw new InvalidOperationException("无法启动 GravityCone CLI。");
            return p;
        });
        _process.Exited += (_, _) => FailAllPending("GravityCone CLI 已退出。");
        _ = ReadOutputAsync(_process);
        _ = ReadErrorAsync(_process);

        using var ready = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ready.Token);
        while (!linked.IsCancellationRequested)
        {
            try
            {
                var response = await RequestAsync("system.ping", null, null, TimeSpan.FromSeconds(2), linked.Token);
                if (response.Status == "success") return;
            }
            catch when (!linked.IsCancellationRequested)
            {
                await Task.Delay(150, linked.Token);
            }
        }
        throw new TimeoutException("GravityCone CLI 启动超时。");
    }

    public async Task<GravityConeResponse> RequestAsync(string method, object? parameters = null,
        IProgress<GravityConeProgress>? progress = null, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (_process is not { HasExited: false } process)
            throw new InvalidOperationException("GravityCone CLI 尚未启动。");

        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<GravityConeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
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

    private static void Trace(string message)
    {
        Console.WriteLine(message);
        Logger.Debug(message);
    }

    public async ValueTask DisposeAsync()
    {
        var process = _process;
        if (process is null) return;
        if (!process.HasExited)
        {
            try
            {
                await RequestAsync("system.shutdown", timeout: TimeSpan.FromSeconds(3));
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(4));
            }
            catch
            {
                if (!process.HasExited) process.Kill(true);
            }
        }
        _process = null;
        process.Dispose();
        _writeLock.Dispose();
    }

    private sealed record PendingRequest(TaskCompletionSource<GravityConeResponse> Completion,
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


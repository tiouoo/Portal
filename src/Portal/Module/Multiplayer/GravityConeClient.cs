using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Portal.Const;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Module.Multiplayer;

public sealed class GravityConeClient : IAsyncDisposable
{
    public const string RelayConfigUrl = "https://cdn.tiouo.cc/portal/multiplayer-relays.json";

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static string RelayConfigCachePath => Path.Combine(ConfigPath.UserDataRootPath, "Multiplayer", "relays.json");

    private readonly ConcurrentDictionary<int, PendingRequest> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Process? _process;
    private int _nextId;

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

    public async Task RestartAsync(GravityConeInstallation installation, CancellationToken cancellationToken)
    {
        var process = _process;
        if (process is { HasExited: false })
        {
            try
            {
                await RequestAsync("system.shutdown", timeout: TimeSpan.FromSeconds(3),
                    cancellationToken: cancellationToken);
                await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(4), cancellationToken);
            }
            catch
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
        }

        _process = null;
        process?.Dispose();
        FailAllPending("GravityCone CLI 正在重启。");
        await StartAsync(installation, cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> GetPeersAsync(CancellationToken cancellationToken)
    {
        try
        {
            var config = await DownloadRelayConfigAsync(cancellationToken);
            await SaveRelayConfigAsync(config, cancellationToken);
            return config.Peers;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
                                   ex is HttpRequestException or JsonException or InvalidDataException or IOException or
                                       OperationCanceledException)
        {
            Logger.Warning($"[Multiplayer] Failed to download relay configuration: {ex.Message}");
            var cachedConfig = await ReadCachedRelayConfigAsync(cancellationToken);
            if (cachedConfig is not null) return cachedConfig.Peers;
            throw new InvalidOperationException("无法获取联机中转服务器配置，请检查网络后重试。", ex);
        }
    }

    private static async Task<RelayConfig> DownloadRelayConfigAsync(CancellationToken cancellationToken)
    {
        await using var stream = await HttpClient.GetStreamAsync(RelayConfigUrl, cancellationToken);
        var config = await JsonSerializer.DeserializeAsync<RelayConfig>(stream, JsonOptions, cancellationToken)
                     ?? throw new InvalidDataException("联机中转服务器配置为空。");
        return ValidateRelayConfig(config);
    }

    private static async Task SaveRelayConfigAsync(RelayConfig config, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RelayConfigCachePath)!);
        await File.WriteAllTextAsync(RelayConfigCachePath, JsonSerializer.Serialize(config), cancellationToken);
    }

    private static async Task<RelayConfig?> ReadCachedRelayConfigAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(RelayConfigCachePath)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(RelayConfigCachePath, cancellationToken);
            var config = JsonSerializer.Deserialize<RelayConfig>(json, JsonOptions);
            return config is null ? null : ValidateRelayConfig(config);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        {
            Logger.Warning($"[Multiplayer] Failed to read cached relay configuration: {ex.Message}");
            return null;
        }
    }

    private static RelayConfig ValidateRelayConfig(RelayConfig config)
    {
        if (config.SchemaVersion != 1) throw new InvalidDataException("不支持的联机中转服务器配置版本。");
        if (config.Peers is null || config.Peers.Count == 0)
            throw new InvalidDataException("联机中转服务器配置中没有可用服务器。");
        if (config.Peers.Any(peer => !IsValidPeer(peer)))
            throw new InvalidDataException("联机中转服务器配置包含无效地址。");
        return config with { Peers = config.Peers.Distinct(StringComparer.Ordinal).ToList() };
    }

    private static bool IsValidPeer(string peer) =>
        Uri.TryCreate(peer, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" or "tcp" or "ws" or "wss" &&
        !string.IsNullOrWhiteSpace(uri.Host);

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

    private sealed record RelayConfig(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("peers")] List<string> Peers);
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

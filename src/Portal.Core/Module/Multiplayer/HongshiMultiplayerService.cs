using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Portal.Core.Const;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Module.Multiplayer;

public enum HongshiStatus
{
    Unsupported,
    Idle,
    WaitingForPort,
    Downloading,
    SelectingNode,
    Starting,
    Open,
    Closed,
    Error
}

public enum HongshiErrorType
{
    Unsupported,
    NodeList,
    NodeUnavailable,
    InvalidPort,
    Install,
    KernelStart,
    KernelExit,
    StatusFile,
    Unknown
}

public sealed record HongshiNode(string Name, string Address, long? LatencyMs, bool Reachable, bool Cached);

public sealed record DetectedLanPort(string InstanceId, string InstanceName, string ProcessId, int Port,
    string DetectedAt);

public sealed class HongshiState
{
    public bool Supported { get; set; }
    public HongshiStatus Status { get; set; }
    public int? LocalPort { get; set; }
    public HongshiNode? Node { get; set; }
    public string? PublicAddress { get; set; }
    public string? CreatedAt { get; set; }
    public int? LastExitCode { get; set; }
    public HongshiErrorType? ErrorType { get; set; }
    public string? ErrorMessage { get; set; }
    public string? BoundInstanceId { get; set; }
    public bool PortChanged { get; set; }
    public bool BinaryInstalled { get; set; }
    public int? DownloadProgress { get; set; }
}

public readonly record struct HongshiDownloadProgress(int? Percent, string? Message);

/// <summary>
/// 红石联机（RedStone / Hongshi）公网联机服务。
/// 负责内核下载、节点探测、隧道创建/关闭，以及从 Minecraft 日志中检测局域网端口。
/// </summary>
public sealed class HongshiMultiplayerService
{
    public const string ApiBase = "https://hongshi.site";
    public const string NodeEndpoint = "https://hongshi.site/newserver.json";
    public const int ControlPort = 7000;

    private const int MaxBinarySize = 256 * 1024 * 1024;
    private static readonly TimeSpan NodeProbeTimeout = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);

    private static readonly Regex[] LanPortPatterns =
    [
        new(@"local game hosted on port\s+(\d{1,5})", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"started serving on(?:\s+port)?\s+(\d{1,5})", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"successfully opened port\s+(\d{1,5})", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    ];

    private readonly object _stateLock = new();
    private readonly object _detectedPortsLock = new();
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly Dictionary<string, DetectedLanPort> _detectedPorts = new();

    private HongshiState _state = new();
    private Process? _child;
    private string? _statusFile;

    private HongshiMultiplayerService()
    {
        _state = new HongshiState
        {
            Supported = IsSupported(),
            Status = IsSupported() ? HongshiStatus.Idle : HongshiStatus.Unsupported,
            BinaryInstalled = BinaryInstalled()
        };
    }

    public static HongshiMultiplayerService Instance { get; } = new();

    public event EventHandler? StateChanged;

    private static HttpClient Client { get; } = new() { Timeout = TimeSpan.FromMinutes(5) };

    private static string Root => Path.Combine(ConfigPath.UserDataRootPath, "Multiplayer", "Redstone");
    public static string LogsDirectory => Path.Combine(Root, "logs");
    private static string BinaryPath => Path.Combine(Root, BinaryName);
    private static string NodeCachePath => Path.Combine(Root, "nodes.json");
    private static string StatusFilePath => Path.Combine(Path.GetTempPath(), "Portal-Hongshi",
        $"tunnel-{Environment.ProcessId}.ini");

    private static string BinaryName => OperatingSystem.IsWindows() ? "hongshi.exe" : "hongshi";

    public HongshiState GetState()
    {
        lock (_stateLock)
        {
            return new HongshiState
            {
                Supported = _state.Supported,
                Status = _state.Status,
                LocalPort = _state.LocalPort,
                Node = _state.Node,
                PublicAddress = _state.PublicAddress,
                CreatedAt = _state.CreatedAt,
                LastExitCode = _state.LastExitCode,
                ErrorType = _state.ErrorType,
                ErrorMessage = _state.ErrorMessage,
                BoundInstanceId = _state.BoundInstanceId,
                PortChanged = _state.PortChanged,
                BinaryInstalled = _state.BinaryInstalled,
                DownloadProgress = _state.DownloadProgress
            };
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _child is { HasExited: false } || _state.Status is HongshiStatus.Starting or HongshiStatus.Open
                    or HongshiStatus.SelectingNode;
            }
        }
    }

    public IReadOnlyList<DetectedLanPort> GetDetectedPorts()
    {
        lock (_detectedPortsLock)
        {
            return [.. _detectedPorts.Values.OrderBy(port => port.InstanceId, StringComparer.Ordinal)];
        }
    }

    private void UpdateState(Action<HongshiState> action)
    {
        lock (_stateLock)
        {
            action(_state);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public static bool IsSupported()
    {
        if (OperatingSystem.IsWindows()) return RuntimeInformation.ProcessArchitecture == Architecture.X64;
        if (OperatingSystem.IsMacOS())
            return RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;
        if (OperatingSystem.IsLinux())
            return RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;
        return false;
    }

    private bool BinaryInstalled()
    {
        try
        {
            return File.Exists(BinaryPath) && ValidBinary(File.ReadAllBytes(BinaryPath));
        }
        catch
        {
            return false;
        }
    }

    private static bool ValidBinary(byte[] bytes)
    {
        if (OperatingSystem.IsWindows())
        {
            if (bytes.Length < 0x40 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z') return false;
            var peOffset = BitConverter.ToInt32(bytes, 0x3c);
            return peOffset >= 0 && peOffset + 4 <= bytes.Length &&
                   bytes[peOffset] == (byte)'P' && bytes[peOffset + 1] == (byte)'E' &&
                   bytes[peOffset + 2] == 0 && bytes[peOffset + 3] == 0;
        }

        if (OperatingSystem.IsLinux()) return bytes.Length >= 4 && bytes[0] == 0x7f && bytes[1] == (byte)'E' &&
                                               bytes[2] == (byte)'L' && bytes[3] == (byte)'F';
        if (OperatingSystem.IsMacOS())
        {
            if (bytes.Length < 4) return false;
            var magic = bytes.AsSpan(0, 4).ToArray();
            return magic.SequenceEqual(new byte[] { 0xfe, 0xed, 0xfa, 0xce }) ||
                   magic.SequenceEqual(new byte[] { 0xce, 0xfa, 0xed, 0xfe }) ||
                   magic.SequenceEqual(new byte[] { 0xfe, 0xed, 0xfa, 0xcf }) ||
                   magic.SequenceEqual(new byte[] { 0xcf, 0xfa, 0xed, 0xfe }) ||
                   magic.SequenceEqual(new byte[] { 0xca, 0xfe, 0xba, 0xbe }) ||
                   magic.SequenceEqual(new byte[] { 0xbe, 0xba, 0xfe, 0xca });
        }

        return false;
    }

    private static string DownloadEndpoint()
    {
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "darwin" : "linux";
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
        return os == "windows"
            ? $"{ApiBase}/api/download/windows"
            : $"{ApiBase}/api/download/{os}?arch={arch}";
    }

    public async Task DownloadAsync(IProgress<HongshiDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            if (!IsSupported())
                throw new PlatformNotSupportedException(
                    CommonLanguageManager.Instance.multiplayer_redstoneUnsupported.CurrentValue());
            if (IsRunning)
                throw new InvalidOperationException(
                    CommonLanguageManager.Instance.multiplayer_redstoneCannotReplaceWhileRunning.CurrentValue());

            UpdateState(state =>
            {
                state.Status = HongshiStatus.Downloading;
                state.DownloadProgress = 0;
                state.ErrorType = null;
                state.ErrorMessage = null;
            });

            var downloadUrl = await RequestDownloadUrlAsync(cancellationToken);

            using var response = await Client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            if (total > MaxBinarySize)
                throw new InvalidDataException(
                    CommonLanguageManager.Instance.multiplayer_redstoneDownloadTooLarge.CurrentValue());

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var memory = new MemoryStream();
            var buffer = new byte[128 * 1024];
            long downloaded = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                memory.Write(buffer, 0, read);
                downloaded += read;
                if (downloaded > MaxBinarySize)
                    throw new InvalidDataException(
                        CommonLanguageManager.Instance.multiplayer_redstoneDownloadTooLarge.CurrentValue());
                if (total > 0)
                {
                    var percent = Math.Clamp((int)(downloaded * 100 / total), 0, 100);
                    UpdateState(state => state.DownloadProgress = percent);
                    progress?.Report(new HongshiDownloadProgress(percent,
                        CommonLanguageManager.Instance.multiplayer_downloading.CurrentValue()));
                }
            }

            var data = memory.ToArray();
            if (data.Length > MaxBinarySize || !ValidBinary(data))
                throw new InvalidDataException(
                    CommonLanguageManager.Instance.multiplayer_redstoneInvalidKernel.CurrentValue());

            Directory.CreateDirectory(Root);
            var temporary = BinaryPath + ".download";
            await File.WriteAllBytesAsync(temporary, data, cancellationToken);
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(temporary);
                File.SetUnixFileMode(temporary, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute |
                                               UnixFileMode.OtherExecute);
            }

            File.Move(temporary, BinaryPath, true);

            UpdateState(state =>
            {
                state.BinaryInstalled = true;
                state.Status = HongshiStatus.Idle;
                state.DownloadProgress = null;
            });
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.Warning($"[RedStone] Download failed: {exception}");
            UpdateState(state =>
            {
                state.Status = HongshiStatus.Error;
                state.DownloadProgress = null;
                state.ErrorType = HongshiErrorType.Install;
                state.ErrorMessage = exception.Message;
            });
            throw;
        }
        finally
        {
            _operation.Release();
        }
    }

    private async Task<string> RequestDownloadUrlAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, DownloadEndpoint());
        using var response = await Client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(string.Format(
                CommonLanguageManager.Instance.multiplayer_redstoneDownloadFailed.CurrentValue(),
                (int)response.StatusCode, body));
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var url = document.RootElement.GetProperty("url").GetString();
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(CommonLanguageManager.Instance.multiplayer_redstoneInvalidDownloadUrl.CurrentValue());
        return url;
    }

    private async Task<(Dictionary<string, string> Nodes, bool Cached)> LoadNodeMapAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, NodeEndpoint);
            using var response = await Client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var nodes = ParseNodeMap(bytes);
            await WriteNodeCacheAsync(nodes, cancellationToken);
            return (nodes, false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.Warning($"[RedStone] Failed to fetch nodes: {exception.Message}");
            if (File.Exists(NodeCachePath))
            {
                try
                {
                    var cached = ParseNodeMap(await File.ReadAllBytesAsync(NodeCachePath, cancellationToken));
                    return (cached, true);
                }
                catch (Exception cacheException) when (!cancellationToken.IsCancellationRequested)
                {
                    Logger.Warning($"[RedStone] Failed to read node cache: {cacheException.Message}");
                }
            }

            throw new InvalidOperationException(
                CommonLanguageManager.Instance.multiplayer_redstoneNodeFetchFailed.CurrentValue());
        }
    }

    private static Dictionary<string, string> ParseNodeMap(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var result = new Dictionary<string, string>();

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String) continue;
                var name = property.Name.Trim();
                var address = property.Value.GetString()?.Trim();
                if (name.Length == 0 || string.IsNullOrEmpty(address)) continue;
                result[name] = ValidateHost(address);
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var element in root.EnumerateArray())
            {
                index++;
                if (element.ValueKind != JsonValueKind.Object) continue;
                var host = element.TryGetProperty("host", out var hostValue) && hostValue.ValueKind == JsonValueKind.String
                    ? hostValue.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(host)) continue;
                var name = element.TryGetProperty("name", out var nameValue) && nameValue.ValueKind == JsonValueKind.String
                    ? nameValue.GetString()
                    : null;
                var region = element.TryGetProperty("region", out var regionValue) && regionValue.ValueKind == JsonValueKind.String
                    ? regionValue.GetString()
                    : null;
                var display = string.IsNullOrWhiteSpace(name)
                    ? (string.IsNullOrWhiteSpace(region) ? $"Node {index}" : region.Trim())
                    : name.Trim();
                result[display] = ValidateHost(host);
            }
        }

        if (result.Count == 0)
            throw new InvalidDataException(CommonLanguageManager.Instance.multiplayer_redstoneEmptyNodeList.CurrentValue());
        return result;
    }

    private static string ValidateHost(string host)
    {
        var value = host.Trim();
        if (value.Length == 0 || value.Length > 253 ||
            value.IndexOfAny(['/', '\\', ':', '?', '#', '@']) >= 0 ||
            value.Any(char.IsWhiteSpace))
            throw new InvalidDataException(string.Format(
                CommonLanguageManager.Instance.multiplayer_redstoneInvalidNodeAddress.CurrentValue(), host));

        if (System.Net.IPAddress.TryParse(value, out var ip))
        {
            if (System.Net.IPAddress.IsLoopback(ip) || ip.Equals(System.Net.IPAddress.Any) ||
                ip.Equals(System.Net.IPAddress.IPv6Any) || ip.Equals(System.Net.IPAddress.IPv6Loopback))
                throw new InvalidDataException(string.Format(
                    CommonLanguageManager.Instance.multiplayer_redstoneInvalidNodeAddress.CurrentValue(), host));
        }
        else if (!value.Split('.').All(label => label.Length > 0 && label.Length <= 63 &&
                                                !label.StartsWith('-') && !label.EndsWith('-') &&
                                                label.All(character => char.IsAsciiLetterOrDigit(character) ||
                                                                       character == '-')))
        {
            throw new InvalidDataException(string.Format(
                CommonLanguageManager.Instance.multiplayer_redstoneInvalidNodeAddress.CurrentValue(), host));
        }

        return value;
    }

    private async Task WriteNodeCacheAsync(Dictionary<string, string> nodes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Root);
        var temporary = NodeCachePath + ".new";
        await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(nodes), cancellationToken);
        File.Move(temporary, NodeCachePath, true);
    }

    private static async Task<HongshiNode> ProbeNodeAsync(string name, string address, bool cached,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var reachable = false;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(NodeProbeTimeout);
            using var client = new TcpClient();
            await client.ConnectAsync(address, ControlPort, cts.Token);
            reachable = true;
        }
        catch
        {
            reachable = false;
        }

        return new HongshiNode(name, address, reachable ? started.ElapsedMilliseconds : null, reachable, cached);
    }

    public async Task<List<HongshiNode>> GetNodesAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var (nodes, cached) = await LoadNodeMapAsync(cancellationToken);

        var tasks = nodes.Select(pair => ProbeNodeAsync(pair.Key, pair.Value, cached, cancellationToken)).ToList();
        var probed = await Task.WhenAll(tasks);
        return probed.OrderBy(node => !node.Reachable)
            .ThenBy(node => node.LatencyMs ?? long.MaxValue)
            .ThenBy(node => node.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task StartAsync(int localPort, string? nodeName, string? instanceId,
        CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            await StartCoreAsync(localPort, nodeName, instanceId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            SetStartError(HongshiErrorType.Unknown, exception.Message);
            throw;
        }
        finally
        {
            _operation.Release();
        }
    }

    private void SetStartError(HongshiErrorType type, string message)
    {
        UpdateState(state =>
        {
            state.Status = HongshiStatus.Error;
            state.PublicAddress = null;
            state.ErrorType = type;
            state.ErrorMessage = message;
        });
    }

    private async Task StartCoreAsync(int localPort, string? nodeName, string? instanceId,
        CancellationToken cancellationToken)
    {
        if (!IsSupported())
            throw new PlatformNotSupportedException(
                CommonLanguageManager.Instance.multiplayer_redstoneUnsupported.CurrentValue());
        if (localPort <= 0 || localPort > 65535)
            throw new ArgumentOutOfRangeException(nameof(localPort),
                CommonLanguageManager.Instance.multiplayer_redstoneInvalidPort.CurrentValue());
        if (_child is { HasExited: false })
            throw new InvalidOperationException(
                CommonLanguageManager.Instance.multiplayer_redstoneAlreadyRunning.CurrentValue());

        if (!string.IsNullOrEmpty(instanceId))
        {
            DetectedLanPort? detected;
            lock (_detectedPortsLock)
            {
                _detectedPorts.TryGetValue(instanceId, out detected);
            }

            if (detected is null)
                throw new InvalidOperationException(
                    CommonLanguageManager.Instance.multiplayer_redstoneInstanceGone.CurrentValue());
            if (detected.Port != localPort)
                throw new InvalidOperationException(
                    CommonLanguageManager.Instance.multiplayer_redstonePortMismatch.CurrentValue());
        }

        var binary = EnsureBinary();

        UpdateState(state =>
        {
            state.Status = HongshiStatus.SelectingNode;
            state.LocalPort = localPort;
            state.Node = null;
            state.PublicAddress = null;
            state.CreatedAt = null;
            state.LastExitCode = null;
            state.ErrorType = null;
            state.ErrorMessage = null;
            state.BoundInstanceId = instanceId;
            state.PortChanged = false;
        });

        var nodes = await GetNodesAsync(false, cancellationToken);
        if (!string.IsNullOrEmpty(nodeName))
            nodes = nodes.Where(node => node.Name == nodeName).ToList();
        else
            nodes = nodes.Where(node => node.Reachable).ToList();
        if (nodes.Count == 0)
            throw new InvalidOperationException(
                CommonLanguageManager.Instance.multiplayer_redstoneNoReachableNode.CurrentValue());

        Directory.CreateDirectory(Root);
        var statusFile = StatusFilePath;
        var parent = Path.GetDirectoryName(statusFile);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        File.Delete(statusFile);
        var startedAt = DateTime.UtcNow;
        var automatic = string.IsNullOrEmpty(nodeName);
        var lastExit = -1;

        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpdateState(state =>
            {
                state.Status = HongshiStatus.Starting;
                state.Node = node;
            });

            Process? child = null;
            try
            {
                var startInfo = new ProcessStartInfo(binary)
                {
                    WorkingDirectory = Root,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("-server");
                startInfo.ArgumentList.Add(node.Address);
                startInfo.ArgumentList.Add("-port");
                startInfo.ArgumentList.Add(localPort.ToString());
                startInfo.ArgumentList.Add("-status-file");
                startInfo.ArgumentList.Add(statusFile);

                child = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                if (!child.Start())
                    throw new InvalidOperationException(
                        CommonLanguageManager.Instance.multiplayer_redstoneCannotStartKernel.CurrentValue());

                var (tunnel, exitCode) = await WaitUntilOpenAsync(child, statusFile, startedAt, cancellationToken);
                if (tunnel is not null)
                {
                    if (!tunnel.Server.Equals(node.Address, StringComparison.Ordinal))
                    {
                        TryKill(child);
                        throw new InvalidOperationException(
                            CommonLanguageManager.Instance.multiplayer_redstoneUnexpectedServer.CurrentValue());
                    }

                    var publicAddress = $"{tunnel.Server}:{tunnel.Port}";
                    UpdateState(state =>
                    {
                        state.Status = HongshiStatus.Open;
                        state.PublicAddress = publicAddress;
                        state.CreatedAt = tunnel.Created;
                        state.LastExitCode = null;
                    });

                    _child = child;
                    _statusFile = statusFile;
                    _ = Task.Run(() => MonitorKernelAsync(child, statusFile, startedAt));
                    return;
                }

                lastExit = exitCode;
                child.Dispose();
                child = null;
                if (!automatic || exitCode != 1) break;
            }
            finally
            {
                child?.Dispose();
            }
        }

        var message = string.Format(
            CommonLanguageManager.Instance.multiplayer_redstoneTunnelFailed.CurrentValue(), lastExit);
        SetStartError(lastExit == 1 ? HongshiErrorType.NodeUnavailable : HongshiErrorType.KernelExit, message);
        throw new InvalidOperationException(message);
    }

    private string EnsureBinary()
    {
        if (!File.Exists(BinaryPath) || !ValidBinary(File.ReadAllBytes(BinaryPath)))
            throw new FileNotFoundException(
                CommonLanguageManager.Instance.multiplayer_redstoneKernelMissing.CurrentValue(), BinaryPath);
        return BinaryPath;
    }

    private async Task<(TunnelStatus? Tunnel, int ExitCode)> WaitUntilOpenAsync(Process child, string statusFile,
        DateTime startedAt, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + StartTimeout;
        while (true)
        {
            if (child.HasExited)
                return (null, child.ExitCode);

            var status = ReadFreshStatus(statusFile, startedAt);
            if (status is not null)
            {
                if (status.Status == "open")
                    return (child.HasExited ? (null, child.ExitCode) : (status, 0));
                return (null, 0);
            }

            if (DateTime.UtcNow >= deadline)
            {
                TryKill(child);
                throw new TimeoutException(
                    CommonLanguageManager.Instance.multiplayer_redstoneTunnelTimeout.CurrentValue());
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    private static TunnelStatus? ReadFreshStatus(string path, DateTime startedAt)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.LastWriteTimeUtc < startedAt) return null;
            return ParseTunnelStatus(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private static TunnelStatus? ParseTunnelStatus(string contents)
    {
        var inTunnel = false;
        var values = new Dictionary<string, string>();
        foreach (var rawLine in contents.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inTunnel = line[1..^1].Equals("tunnel", StringComparison.Ordinal);
                continue;
            }

            if (inTunnel && line.Contains('='))
            {
                var separator = line.IndexOf('=');
                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Split(';')[0].Trim();
                values[key] = value;
            }
        }

        var status = values.GetValueOrDefault("status");
        if (status is not ("open" or "closed")) return null;
        var server = values.GetValueOrDefault("server") ?? string.Empty;
        if (status == "open" && server.Length == 0) return null;
        var portText = values.GetValueOrDefault("port");
        if (!int.TryParse(portText, out var port)) return null;
        if (status == "open" && port is < 1 or > 65535) return null;
        if (status == "closed" && port != -1) return null;
        return new TunnelStatus(status, server, port, values.GetValueOrDefault("created"));
    }

    private async Task MonitorKernelAsync(Process child, string statusFile, DateTime startedAt)
    {
        try
        {
            await child.WaitForExitAsync();
            var exitCode = child.ExitCode;
            if (!ReferenceEquals(_child, child)) return;
            UpdateState(state =>
            {
                if (exitCode == 0)
                {
                    state.Status = HongshiStatus.Closed;
                    state.PublicAddress = null;
                    state.LastExitCode = exitCode;
                }
                else
                {
                    state.Status = HongshiStatus.Error;
                    state.PublicAddress = null;
                    state.ErrorType = HongshiErrorType.KernelExit;
                    state.ErrorMessage = string.Format(
                        CommonLanguageManager.Instance.multiplayer_redstoneKernelExited.CurrentValue(), exitCode);
                    state.LastExitCode = exitCode;
                }
            });
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_child, child))
                UpdateState(state =>
                {
                    state.Status = HongshiStatus.Error;
                    state.ErrorType = HongshiErrorType.KernelExit;
                    state.ErrorMessage = exception.Message;
                });
        }
        finally
        {
            if (ReferenceEquals(_child, child))
            {
                _child = null;
                _statusFile = null;
            }
        }
    }

    public async Task StopAsync()
    {
        await _operation.WaitAsync();
        try
        {
            var child = _child;
            _child = null;
            _statusFile = null;
            if (child is not null)
            {
                TryKill(child);
                try
                {
                    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                }
                catch
                {
                    // Best effort wait.
                }

                child.Dispose();
            }

            ResetStateInternal();
        }
        finally
        {
            _operation.Release();
        }
    }

    private void ResetStateInternal()
    {
        lock (_stateLock)
        {
            _state = new HongshiState
            {
                Supported = IsSupported(),
                Status = IsSupported() ? HongshiStatus.Idle : HongshiStatus.Unsupported,
                BinaryInstalled = BinaryInstalled()
            };
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ObserveMinecraftLog(string instanceId, string instanceName, string processId, string message)
    {
        var port = 0;
        foreach (var pattern in LanPortPatterns)
        {
            var match = pattern.Match(message);
            if (match.Success && int.TryParse(match.Groups[1].Value, out port) && port > 0) break;
            port = 0;
        }

        if (port <= 0) return;

        lock (_detectedPortsLock)
        {
            _detectedPorts[instanceId] = new DetectedLanPort(instanceId, instanceName, processId, port,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        UpdateState(state =>
        {
            if (state.BoundInstanceId == instanceId &&
                state.LocalPort is not null && state.LocalPort != port)
                state.PortChanged = true;
        });
    }

    public void MinecraftProcessFinished(string instanceId)
    {
        lock (_detectedPortsLock)
        {
            _detectedPorts.Remove(instanceId);
        }

        var shouldStop = false;
        lock (_stateLock)
        {
            shouldStop = _state.BoundInstanceId == instanceId &&
                         _state.Status is HongshiStatus.Starting or HongshiStatus.Open;
        }

        if (shouldStop)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await StopAsync();
                }
                catch (Exception exception)
                {
                    Logger.Warning($"[RedStone] Failed to stop after Minecraft exited: {exception.Message}");
                }
            });
        }
    }

    private static void TryKill(Process? process)
    {
        if (process is null || process.HasExited) return;
        try
        {
            process.Kill(true);
        }
        catch (Exception exception)
        {
            Logger.Debug($"[RedStone] Failed to kill process: {exception.Message}");
        }
    }

    private sealed record TunnelStatus(string Status, string Server, int Port, string? Created);
}

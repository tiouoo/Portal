using System.Collections.Concurrent;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Portal.Core.Const;
using Portal.Core.Module.Update;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Module.Multiplayer;

public enum TerracottaMultiplayerStatus
{
    Idle,
    Starting,
    Downloading,
    Waiting,
    HostScanning,
    HostStarting,
    HostReady,
    GuestConnecting,
    GuestStarting,
    GuestReady,
    Error,
    Fatal
}

public enum TerracottaDownloadStage
{
    Preparing,
    Downloading,
    Verifying,
    Extracting,
    Installing,
    Complete
}

public enum TerracottaErrorType
{
    Os,
    Network,
    Install,
    Terracotta,
    Unknown
}

public sealed record TerracottaPlayer(string MachineId, string Name, string Vendor, string Kind);

public sealed class TerracottaState
{
    public TerracottaMultiplayerStatus Status { get; set; } = TerracottaMultiplayerStatus.Idle;
    public int? HttpPort { get; set; }
    public string? RoomCode { get; set; }
    public int? ServerPort { get; set; }
    public List<TerracottaPlayer> Players { get; set; } = [];
    public int? DownloadProgress { get; set; }
    public TerracottaDownloadStage? DownloadStage { get; set; }
    public bool BinaryInstalled { get; set; }
    public string? InstalledVersion { get; set; }
    public TerracottaErrorType? ErrorType { get; set; }
    public string? ErrorMessage { get; set; }
    public int? ProfileIndex { get; set; }
}

public sealed class TerracottaUpdateInfo
{
    public string? InstalledVersion { get; set; }
    public string LatestVersion { get; set; } = string.Empty;
    public bool UpdateAvailable { get; set; }
}

public readonly record struct TerracottaDownloadProgress(double? Fraction, string? Message);

/// <summary>
/// 陶瓦联机（Terracotta）多人在线服务。
/// 负责核心下载、服务启停、房间创建/加入、状态轮询与日志获取。
/// </summary>
public sealed class TerracottaMultiplayerService
{
    public const int MaxArchiveSize = 256 * 1024 * 1024;
    public const int MaxDiagnosticLines = 2000;
    private const int MaxConsecutivePollFailures = 3;
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(10);

    private static readonly string[] PublicNodeSchemes = ["http", "https", "tcp", "tls", "udp", "ws", "wss"];

    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly ConcurrentQueue<string> _diagnosticOutput = new();

    private TerracottaState _state = new();
    private Process? _process;
    private CancellationTokenSource? _pollerCancellation;

    private TerracottaMultiplayerService()
    {
    }

    public static TerracottaMultiplayerService Instance { get; } = new();

    public event EventHandler? StateChanged;

    private static HttpClient Client { get; } = new() { Timeout = TimeSpan.FromMinutes(5) };

    private static HttpClient ApiClient { get; } = new(new SocketsHttpHandler { UseProxy = false })
    {
        Timeout = ApiTimeout
    };

    private static string Root => Path.Combine(ConfigPath.UserDataRootPath, "Multiplayer", "Terracotta");
    private static string VersionFile => Path.Combine(Root, "terracotta-version.json");
    private static string PortFile => Path.Combine(Path.GetTempPath(), $"terracotta_port_{Environment.ProcessId}.json");
    private static string InstalledBinaryPath => Path.Combine(Root, BinaryName);

    private static string BinaryName => OperatingSystem.IsWindows() ? "terracotta.exe" : "terracotta";

    public TerracottaState GetState()
    {
        lock (_stateLock)
        {
            var snapshot = new TerracottaState
            {
                Status = _state.Status,
                HttpPort = _state.HttpPort,
                RoomCode = _state.RoomCode,
                ServerPort = _state.ServerPort,
                DownloadProgress = _state.DownloadProgress,
                DownloadStage = _state.DownloadStage,
                BinaryInstalled = _state.BinaryInstalled,
                InstalledVersion = _state.InstalledVersion,
                ErrorType = _state.ErrorType,
                ErrorMessage = _state.ErrorMessage,
                ProfileIndex = _state.ProfileIndex,
                Players = [.. _state.Players]
            };
            return snapshot;
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _state.HttpPort is not null || _process is { HasExited: false };
            }
        }
    }

    public string PlatformKey => GetPlatformKey();

    private void UpdateState(Action<TerracottaState> action)
    {
        lock (_stateLock)
        {
            action(_state);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RecordOutput(string stream, string line)
    {
        var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{stream}] {line}";
        _diagnosticOutput.Enqueue(entry);
        while (_diagnosticOutput.Count > MaxDiagnosticLines)
            _diagnosticOutput.TryDequeue(out _);
    }

    private static string GetPlatformKey()
    {
        if (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            return "windows-x86_64";
        if (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            return "windows-arm64";
        if (OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            return "linux-x86_64";
        if (OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            return "linux-arm64";
        if (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            return "macos-x86_64";
        if (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            return "macos-arm64";
        return "unsupported";
    }

    private static string VersionedBinaryName(string version, string platform)
    {
        var extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        return $"terracotta-{version}-{platform}{extension}";
    }

    private static IEnumerable<string> DownloadUrls(string version, string platform)
    {
        var artifact = $"terracotta-{version}-{platform}-pkg.tar.gz";
        yield return $"https://gitee.com/burningtnt/Terracotta/releases/download/v{version}/{artifact}";
        yield return GithubMirror.Apply(
            $"https://github.com/burningtnt/Terracotta/releases/download/v{version}/{artifact}");
    }

    private async Task<string> FetchLatestVersionAsync(CancellationToken cancellationToken)
    {
        var endpoints = new[]
        {
            "https://gitee.com/api/v5/repos/burningtnt/Terracotta/releases/latest",
            "https://api.github.com/repos/burningtnt/Terracotta/releases/latest"
        };
        var failures = new List<string>();
        foreach (var endpoint in endpoints)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.UserAgent.ParseAdd(Data.Instance.UserAgent);
                using var response = await Client.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    failures.Add($"{endpoint}: HTTP {(int)response.StatusCode}");
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var tag = document.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
                return tag.TrimStart('v');
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                failures.Add($"{endpoint}: {exception.Message}");
            }
        }

        throw new InvalidOperationException(string.Format(
            CommonLanguageManager.Instance.multiplayer_terracottaFetchVersionFailed.CurrentValue(),
            string.Join("; ", failures)));
    }

    public bool IsBinaryInstalled()
    {
        return IsTerracottaExecutable(InstalledBinaryPath);
    }

    public string? GetInstalledVersion()
    {
        if (!IsTerracottaExecutable(InstalledBinaryPath)) return null;
        try
        {
            if (!File.Exists(VersionFile)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(VersionFile));
            var version = document.RootElement.GetProperty("version").GetString();
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch
        {
            return null;
        }
    }

    public async Task<TerracottaUpdateInfo> CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        var latestVersion = await FetchLatestVersionAsync(cancellationToken);
        var installedVersion = GetInstalledVersion();
        return new TerracottaUpdateInfo
        {
            InstalledVersion = installedVersion,
            LatestVersion = latestVersion,
            UpdateAvailable = installedVersion != latestVersion
        };
    }

    public async Task UpdateAsync(CancellationToken cancellationToken)
    {
        var latest = await CheckForUpdateAsync(cancellationToken);
        if (latest.UpdateAvailable)
            await DownloadAsync(latest.LatestVersion, cancellationToken);
    }

    public async Task DownloadAsync(string? version, CancellationToken cancellationToken)
        => await DownloadAsync(version, null, cancellationToken);

    public async Task DownloadAsync(string? version, IProgress<TerracottaDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            await DownloadCoreAsync(version, progress, cancellationToken);
        }
        finally
        {
            _operation.Release();
        }
    }

    private async Task DownloadCoreAsync(string? version, IProgress<TerracottaDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (IsRunning)
            throw new InvalidOperationException(
                CommonLanguageManager.Instance.multiplayer_terracottaCannotReplaceWhileRunning.CurrentValue());

        try
        {
            UpdateState(state =>
            {
                state.Status = TerracottaMultiplayerStatus.Downloading;
                state.DownloadProgress = 0;
                state.DownloadStage = TerracottaDownloadStage.Preparing;
                state.ErrorType = null;
                state.ErrorMessage = null;
            });
            progress?.Report(new TerracottaDownloadProgress(null,
                CommonLanguageManager.Instance.multiplayer_terracottaConnecting.CurrentValue()));

            var resolvedVersion = version ?? await FetchLatestVersionAsync(cancellationToken);
            ValidateVersion(resolvedVersion);

            var platform = GetPlatformKey();
            if (platform == "unsupported")
                throw new PlatformNotSupportedException(string.Format(
                    CommonLanguageManager.Instance.multiplayer_terracottaUnsupportedPlatform.CurrentValue(),
                    $"{RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture}"));

            byte[]? archiveData = null;
            foreach (var url in DownloadUrls(resolvedVersion, platform))
            {
                Logger.Info($"[Terracotta] Attempting to download from {url}");
                try
                {
                    UpdateState(state => state.DownloadStage = TerracottaDownloadStage.Downloading);
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    using var response =
                        await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        Logger.Warning($"[Terracotta] Download from {url} returned HTTP {(int)response.StatusCode}");
                        continue;
                    }

                    var totalSize = response.Content.Headers.ContentLength ?? 0;
                    if (totalSize > MaxArchiveSize)
                    {
                        Logger.Warning($"[Terracotta] Archive from {url} is too large: {totalSize} bytes");
                        continue;
                    }

                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var hasher = SHA512.Create();
                    using var memory = new MemoryStream();
                    var buffer = new byte[128 * 1024];
                    long downloaded = 0;
                    while (true)
                    {
                        var read = await stream.ReadAsync(buffer, cancellationToken);
                        if (read == 0) break;
                        hasher.TransformBlock(buffer, 0, read, null, 0);
                        memory.Write(buffer, 0, read);
                        downloaded += read;
                        if (downloaded > MaxArchiveSize)
                            throw new InvalidDataException(
                                CommonLanguageManager.Instance.multiplayer_terracottaArchiveTooLarge.CurrentValue());
                        if (totalSize > 0)
                        {
                            var percent = Math.Clamp((int)(downloaded * 100 / totalSize), 0, 100);
                            UpdateState(state => state.DownloadProgress = percent);
                            progress?.Report(new TerracottaDownloadProgress(downloaded / (double)totalSize, null));
                        }
                    }

                    hasher.TransformFinalBlock([], 0, 0);
                    var computedHash = Convert.ToHexString(hasher.Hash!);

                    UpdateState(state => state.DownloadStage = TerracottaDownloadStage.Verifying);
                    try
                    {
                        using var hashRequest = new HttpRequestMessage(HttpMethod.Get, url + ".sha512");
                        using var hashResponse = await Client.SendAsync(hashRequest, cancellationToken);
                        if (hashResponse.IsSuccessStatusCode)
                        {
                            var checksum = (await hashResponse.Content.ReadAsStringAsync(cancellationToken)).Trim();
                            var expected = checksum.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                            if (expected is null || expected.Length != 128 ||
                                !computedHash.Equals(expected, StringComparison.OrdinalIgnoreCase))
                            {
                                Logger.Warning($"[Terracotta] SHA-512 mismatch for {url}");
                                continue;
                            }
                        }
                    }
                    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        Logger.Warning($"[Terracotta] Failed to fetch checksum for {url}: {exception.Message}");
                    }

                    archiveData = memory.ToArray();
                    break;
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    Logger.Warning($"[Terracotta] Download from {url} failed: {exception.Message}");
                }
            }

            if (archiveData is null)
                throw new InvalidOperationException(string.Format(
                    CommonLanguageManager.Instance.multiplayer_terracottaAllSourcesFailed.CurrentValue(),
                    resolvedVersion));

            UpdateState(state =>
            {
                state.DownloadStage = TerracottaDownloadStage.Extracting;
                state.DownloadProgress = null;
            });

            Directory.CreateDirectory(Root);
            var stagingDir = Path.Combine(Root, "extract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDir);
            try
            {
                await Task.Run(() =>
                {
                    using var gzip = new GZipStream(new MemoryStream(archiveData), CompressionMode.Decompress);
                    TarFile.ExtractToDirectory(gzip, stagingDir, true);
                }, cancellationToken);

                UpdateState(state => state.DownloadStage = TerracottaDownloadStage.Installing);

                var expectedName = VersionedBinaryName(resolvedVersion, platform);
                var candidate = FindTerracottaExecutable(stagingDir, expectedName);
                if (candidate is null)
                    throw new InvalidDataException(
                        CommonLanguageManager.Instance.multiplayer_terracottaNoExecutable.CurrentValue());

                if (!OperatingSystem.IsWindows())
                {
                    var mode = File.GetUnixFileMode(candidate);
                    File.SetUnixFileMode(candidate, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute |
                                                   UnixFileMode.OtherExecute);
                }

                await InstallBinaryAsync(candidate, InstalledBinaryPath, cancellationToken);
                if (!IsTerracottaExecutable(InstalledBinaryPath))
                    throw new InvalidDataException(
                        CommonLanguageManager.Instance.multiplayer_terracottaInvalidBinary.CurrentValue());

                await PersistVersionAsync(resolvedVersion, cancellationToken);
                CleanupOldFiles();
            }
            finally
            {
                try
                {
                    if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
                }
                catch (Exception exception)
                {
                    Logger.Warning($"[Terracotta] Failed to clean staging directory: {exception.Message}");
                }
            }

            UpdateState(state =>
            {
                state.DownloadProgress = 100;
                state.DownloadStage = TerracottaDownloadStage.Complete;
            });
            await Task.Delay(300, cancellationToken);
            UpdateState(state =>
            {
                state.Status = TerracottaMultiplayerStatus.Idle;
                state.DownloadProgress = null;
                state.DownloadStage = null;
                state.BinaryInstalled = true;
                state.InstalledVersion = resolvedVersion;
            });
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.Warning($"[Terracotta] Download failed: {exception}");
            UpdateState(state =>
            {
                state.Status = TerracottaMultiplayerStatus.Error;
                state.DownloadProgress = null;
                state.DownloadStage = null;
                state.ErrorType = TerracottaErrorType.Install;
                state.ErrorMessage = exception.Message;
            });
            throw;
        }
    }

    private static void ValidateVersion(string version)
    {
        if (string.IsNullOrEmpty(version) || version.Length > 64 || version.Contains("..", StringComparison.Ordinal) ||
            !version.All(character => char.IsAsciiLetterOrDigit(character) ||
                                      character is '.' or '-' or '_'))
            throw new ArgumentException($"Invalid Terracotta version: {version}");
    }

    private static bool IsTerracottaExecutable(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 4) return false;
            using var stream = File.OpenRead(path);
            Span<byte> magic = stackalloc byte[4];
            if (stream.Read(magic) < 4) return false;

            if (OperatingSystem.IsWindows()) return magic[0] == (byte)'M' && magic[1] == (byte)'Z';
            if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
                return magic[0] == 0x7f && magic[1] == (byte)'E' && magic[2] == (byte)'L' && magic[3] == (byte)'F';
            if (OperatingSystem.IsMacOS())
            {
                return magic.SequenceEqual(new byte[] { 0xce, 0xfa, 0xed, 0xfe }) ||
                       magic.SequenceEqual(new byte[] { 0xcf, 0xfa, 0xed, 0xfe }) ||
                       magic.SequenceEqual(new byte[] { 0xfe, 0xed, 0xfa, 0xce }) ||
                       magic.SequenceEqual(new byte[] { 0xfe, 0xed, 0xfa, 0xcf }) ||
                       magic.SequenceEqual(new byte[] { 0xca, 0xfe, 0xba, 0xbe }) ||
                       magic.SequenceEqual(new byte[] { 0xbe, 0xba, 0xfe, 0xca });
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindTerracottaExecutable(string directory, string preferredName)
    {
        string? preferred = null;
        string? fallback = null;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (!name.Equals(BinaryName, StringComparison.Ordinal) &&
                !name.StartsWith("terracotta-", StringComparison.Ordinal))
                continue;
            if (!IsTerracottaExecutable(file)) continue;

            if (name.Equals(preferredName, StringComparison.Ordinal))
            {
                preferred = file;
                break;
            }

            fallback ??= file;
        }

        return preferred ?? fallback;
    }

    private static async Task InstallBinaryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        var backup = destination + ".old";
        try
        {
            if (File.Exists(backup)) File.Delete(backup);
            if (File.Exists(destination)) File.Move(destination, backup);
            await Task.Run(() => File.Move(source, destination), cancellationToken);
            if (File.Exists(backup)) File.Delete(backup);
        }
        catch
        {
            if (File.Exists(backup) && !File.Exists(destination))
                File.Move(backup, destination);
            throw;
        }
    }

    private async Task PersistVersionAsync(string version, CancellationToken cancellationToken)
    {
        var contents = JsonSerializer.Serialize(new { version });
        var temporary = VersionFile + ".tmp";
        await File.WriteAllTextAsync(temporary, contents, cancellationToken);
        File.Move(temporary, VersionFile, true);
    }

    private void CleanupOldFiles()
    {
        try
        {
            if (!Directory.Exists(Root)) return;
            foreach (var file in Directory.EnumerateFiles(Root))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("terracotta-", StringComparison.Ordinal) &&
                    name != "terracotta-version.json" && name != "terracotta-version.json.tmp" &&
                    name != BinaryName ||
                    name.EndsWith(".tar.gz", StringComparison.Ordinal) ||
                    name.EndsWith(".old", StringComparison.Ordinal))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception exception)
                    {
                        Logger.Warning($"[Terracotta] Failed to clean up {file}: {exception.Message}");
                    }
                }
            }
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Terracotta] Cleanup failed: {exception.Message}");
        }
    }

    public async Task StartAsync(bool autoDownload, CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
                throw new InvalidOperationException(
                    CommonLanguageManager.Instance.multiplayer_terracottaAlreadyRunning.CurrentValue());

            var binaryPath = InstalledBinaryPath;
            if (!IsTerracottaExecutable(binaryPath) && autoDownload)
            {
                await DownloadCoreAsync(null, null, cancellationToken);
                binaryPath = InstalledBinaryPath;
            }

            if (!IsTerracottaExecutable(binaryPath))
                throw new FileNotFoundException(string.Format(
                    CommonLanguageManager.Instance.multiplayer_terracottaBinaryNotFound.CurrentValue(), binaryPath));

            _process = SpawnProcess(binaryPath);
            try
            {
                UpdateState(state =>
                {
                    state.Status = TerracottaMultiplayerStatus.Starting;
                    state.HttpPort = null;
                    state.RoomCode = null;
                    state.ServerPort = null;
                    state.Players.Clear();
                    state.ErrorType = null;
                    state.ErrorMessage = null;
                    state.ProfileIndex = null;
                });

                var port = await WaitForPortAsync(_process, cancellationToken);
                lock (_stateLock)
                {
                    _state.HttpPort = port;
                    _state.Status = TerracottaMultiplayerStatus.Idle;
                }

                StateChanged?.Invoke(this, EventArgs.Empty);
                StartPoller(port);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryKill(_process);
                _process = null;
                throw;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(_process);
                _process = null;
                SetStartError(exception.Message);
                throw;
            }
        }
        finally
        {
            _operation.Release();
        }
    }

    private Process SpawnProcess(string binaryPath)
    {
        Directory.CreateDirectory(Root);
        File.Delete(PortFile);
        var startInfo = new ProcessStartInfo(binaryPath)
        {
            WorkingDirectory = Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (OperatingSystem.IsMacOS())
        {
            startInfo.ArgumentList.Add("--daemon");
        }
        else
        {
            startInfo.ArgumentList.Add("--hmcl");
            startInfo.ArgumentList.Add(PortFile);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
            throw new InvalidOperationException(string.Format(
                CommonLanguageManager.Instance.common_cannotStart.CurrentValue(), BinaryName));

        CaptureOutput(process);
        Logger.Info($"[Terracotta] Started process (pid {process.Id})");
        return process;
    }

    private void CaptureOutput(Process process)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardOutput.ReadLineAsync() is { } line)
                {
                    RecordOutput("stdout", line);
                    Logger.Info($"[Terracotta pid={process.Id} stdout] {line}");
                }
            }
            catch (Exception exception)
            {
                Logger.Debug($"[Terracotta] stdout reader ended: {exception.Message}");
            }
        });
        _ = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardError.ReadLineAsync() is { } line)
                {
                    RecordOutput("stderr", line);
                    Logger.Warning($"[Terracotta pid={process.Id} stderr] {line}");
                }
            }
            catch (Exception exception)
            {
                Logger.Debug($"[Terracotta] stderr reader ended: {exception.Message}");
            }
        });
    }

    private async Task<int> WaitForPortAsync(Process process, CancellationToken cancellationToken)
    {
        var attempts = 0;
        const int maxAttempts = 30;
        while (true)
        {
            await Task.Delay(500, cancellationToken);
            attempts++;

            var port = TryReadPortFile();
            if (port is not null)
            {
                File.Delete(PortFile);
                return port.Value;
            }

            if (process.HasExited)
            {
                if (TryReadPortFile() is { } latePort)
                {
                    File.Delete(PortFile);
                    return latePort;
                }

                var output = string.Join(Environment.NewLine, _diagnosticOutput.TakeLast(50));
                var message = output.Length > 0
                    ? string.Format(CommonLanguageManager.Instance.multiplayer_terracottaExited.CurrentValue(),
                        process.ExitCode, output)
                    : string.Format(CommonLanguageManager.Instance.multiplayer_terracottaExited.CurrentValue(),
                        process.ExitCode, string.Empty);
                SetStartError(message);
                throw new InvalidOperationException(message);
            }

            if (attempts > maxAttempts)
            {
                var message = CommonLanguageManager.Instance.multiplayer_terracottaStartTimeout.CurrentValue();
                SetStartError(message);
                throw new TimeoutException(message);
            }
        }
    }

    private void SetStartError(string message)
    {
        UpdateState(state =>
        {
            state.Status = TerracottaMultiplayerStatus.Error;
            state.ErrorType = TerracottaErrorType.Terracotta;
            state.ErrorMessage = message;
        });
    }

    private static int? TryReadPortFile()
    {
        try
        {
            if (!File.Exists(PortFile)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(PortFile));
            if (!document.RootElement.TryGetProperty("port", out var portElement) ||
                !portElement.TryGetInt32(out var port))
                return null;
            return port;
        }
        catch
        {
            return null;
        }
    }

    private void StartPoller(int port)
    {
        _pollerCancellation?.Cancel();
        _pollerCancellation = new CancellationTokenSource();
        var token = _pollerCancellation.Token;
        _ = Task.Run(async () =>
        {
            var lastIndex = 0u;
            var consecutiveFailures = 0;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(500, token);
                    try
                    {
                        var apiState = await GetApiStateAsync(port, token);
                        consecutiveFailures = 0;
                        var newIndex = apiState.Index;
                        if (newIndex > 0 && newIndex <= lastIndex) continue;
                        lastIndex = newIndex;

                        var serverPort = apiState.State == "guest-ok" && apiState.Url is not null
                            ? ParseServerPort(apiState.Url)
                            : null;

                        UpdateState(state =>
                        {
                            state.HttpPort = port;
                            state.RoomCode = apiState.Room;
                            state.ServerPort = serverPort;
                            state.ProfileIndex = apiState.ProfileIndex;

                            var parsed = ParseStatus(apiState.State);
                            state.Status = parsed ?? TerracottaMultiplayerStatus.Error;
                            if (parsed is null)
                            {
                                state.ErrorType = TerracottaErrorType.Terracotta;
                                state.ErrorMessage = string.Format(
                                    CommonLanguageManager.Instance.multiplayer_terracottaUnknownState.CurrentValue(),
                                    apiState.State);
                            }
                            else if (parsed == TerracottaMultiplayerStatus.Fatal)
                            {
                                state.ErrorType = apiState.Type is { } type && Enum.IsDefined((TerracottaErrorType)type)
                                    ? (TerracottaErrorType)type
                                    : TerracottaErrorType.Unknown;
                                state.ErrorMessage = apiState.Url;
                            }
                            else if (parsed != TerracottaMultiplayerStatus.Error)
                            {
                                state.ErrorType = null;
                                state.ErrorMessage = null;
                            }

                            if (apiState.Profiles is not null)
                            {
                                state.Players = apiState.Profiles
                                    .Select(profile => new TerracottaPlayer(profile.MachineId, profile.Name,
                                        profile.Vendor, profile.Kind))
                                    .ToList();
                            }
                        });
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception exception) when (!token.IsCancellationRequested)
                    {
                        Logger.Warning($"[Terracotta] Failed to poll state: {exception.Message}");
                        consecutiveFailures++;
                        if (consecutiveFailures < MaxConsecutivePollFailures) continue;

                        UpdateState(state =>
                        {
                            if (state.Status != TerracottaMultiplayerStatus.Idle)
                                state.Status = TerracottaMultiplayerStatus.Error;
                            state.HttpPort = null;
                            state.ServerPort = null;
                        });
                        TryKill(_process);
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The poller was cancelled.
            }
        }, token);
    }

    private static TerracottaMultiplayerStatus? ParseStatus(string value)
    {
        return value switch
        {
            "idle" => TerracottaMultiplayerStatus.Idle,
            "starting" => TerracottaMultiplayerStatus.Starting,
            "waiting" => TerracottaMultiplayerStatus.Waiting,
            "host-scanning" => TerracottaMultiplayerStatus.HostScanning,
            "host-starting" => TerracottaMultiplayerStatus.HostStarting,
            "host-ok" => TerracottaMultiplayerStatus.HostReady,
            "guest-connecting" => TerracottaMultiplayerStatus.GuestConnecting,
            "guest-starting" => TerracottaMultiplayerStatus.GuestStarting,
            "guest-ok" => TerracottaMultiplayerStatus.GuestReady,
            "exception" => TerracottaMultiplayerStatus.Error,
            "fatal" => TerracottaMultiplayerStatus.Fatal,
            _ => null
        };
    }

    private static int? ParseServerPort(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (System.Net.IPEndPoint.TryParse(url, out var endpoint)) return endpoint.Port;
        return System.Net.IPAddress.TryParse(url, out _) ? 25565 : null;
    }

    private static readonly JsonSerializerOptions ApiJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private async Task<TerracottaApiState> GetApiStateAsync(int port, CancellationToken cancellationToken)
    {
        var json = await ApiClient.GetStringAsync($"http://127.0.0.1:{port}/state", cancellationToken);
        return JsonSerializer.Deserialize<TerracottaApiState>(json, ApiJsonOptions) ?? throw new InvalidDataException(
            CommonLanguageManager.Instance.multiplayer_terracottaEmptyState.CurrentValue());
    }

    public async Task StopAsync()
    {
        await _operation.WaitAsync();
        try
        {
            _pollerCancellation?.Cancel();
            _pollerCancellation = null;

            var process = _process;
            if (process is { HasExited: false })
                TryKill(process);
            _process = null;

            int? port;
            lock (_stateLock)
            {
                port = _state.HttpPort ?? TryReadPortFile();
            }

            if (port is not null)
            {
                try
                {
                    await ApiClient.GetAsync($"http://127.0.0.1:{port}/panic?peaceful=true")
                        .WaitAsync(TimeSpan.FromSeconds(3));
                }
                catch
                {
                    // The daemon may already be gone.
                }
            }

            ResetStateInternal();
        }
        finally
        {
            _operation.Release();
        }
    }

    public async Task HostAsync(string playerName, string? roomCode, CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            var name = playerName?.Trim() ?? string.Empty;
            if (name.Length == 0)
                throw new ArgumentException(CommonLanguageManager.Instance.multiplayer_enterPlayerName.CurrentValue());
            var port = GetPortOrThrow();
            var nodes = GetConfiguredPublicNodes();
            var url = BuildRoomUrl(port, "/state/scanning", roomCode ?? string.Empty, name, nodes);
            await SendStateRequestAsync(url, cancellationToken);
        }
        finally
        {
            _operation.Release();
        }
    }

    public async Task JoinAsync(string playerName, string roomCode, CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            var name = playerName?.Trim() ?? string.Empty;
            if (name.Length == 0)
                throw new ArgumentException(CommonLanguageManager.Instance.multiplayer_enterPlayerName.CurrentValue());
            var code = ParseRoomCode(roomCode);
            var port = GetPortOrThrow();
            var nodes = GetConfiguredPublicNodes();
            var url = BuildRoomUrl(port, "/state/guesting", code, name, nodes);
            await SendStateRequestAsync(url, cancellationToken);
        }
        finally
        {
            _operation.Release();
        }
    }

    public async Task ResetStateAsync(CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            int? port;
            lock (_stateLock)
            {
                port = _state.HttpPort;
            }

            if (port is not null)
            {
                try
                {
                    var response = await ApiClient.GetAsync($"http://127.0.0.1:{port}/state/ide", cancellationToken);
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException(string.Format(
                            CommonLanguageManager.Instance.multiplayer_terracottaResetFailed.CurrentValue(),
                            (int)response.StatusCode));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
            }

            ResetStateInternal();
        }
        finally
        {
            _operation.Release();
        }
    }

    public async Task<string> GetLogsAsync(CancellationToken cancellationToken)
    {
        var port = GetPortOrThrow();
        return await ApiClient.GetStringAsync($"http://127.0.0.1:{port}/log?fetch=", cancellationToken);
    }

    public string GetDiagnosticReport()
    {
        var state = GetState();
        var stateJson = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        var output = _diagnosticOutput.Count > 0
            ? string.Join(Environment.NewLine, _diagnosticOutput)
            : CommonLanguageManager.Instance.multiplayer_terracottaNoOutput.CurrentValue();
        return string.Format(CommonLanguageManager.Instance.multiplayer_terracottaDiagnosticReport.CurrentValue(),
            GetPlatformKey(), InstalledBinaryPath, stateJson, output);
    }

    private int GetPortOrThrow()
    {
        lock (_stateLock)
        {
            if (_state.HttpPort is not { } port)
                throw new InvalidOperationException(
                    CommonLanguageManager.Instance.multiplayer_terracottaNotRunning.CurrentValue());
            return port;
        }
    }

    private async Task SendStateRequestAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await ApiClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(string.Format(
                CommonLanguageManager.Instance.multiplayer_terracottaRequestFailed.CurrentValue(),
                (int)response.StatusCode, body));
        }
    }

    private static string BuildRoomUrl(int port, string path, string room, string player, IReadOnlyList<string> nodes)
    {
        var builder = new StringBuilder();
        builder.Append($"http://127.0.0.1:{port}{path}?room={Uri.EscapeDataString(room)}&player={Uri.EscapeDataString(player)}");
        foreach (var node in nodes)
        {
            builder.Append("&public_nodes=");
            builder.Append(Uri.EscapeDataString(node));
        }

        return builder.ToString();
    }

    public static string ParseRoomCode(string code)
    {
        if (code.StartsWith("U/", StringComparison.OrdinalIgnoreCase))
        {
            var inner = code[2..];
            if (inner.Length == 19 && inner.Count(character => character == '-') == 3)
            {
                var segments = inner.Split('-');
                if (segments.Length == 4 &&
                    segments.All(segment => segment.Length == 4) &&
                    segments.All(segment => segment.All(char.IsAsciiLetterOrDigit)))
                    return $"U/{inner}";
            }
        }

        throw new ArgumentException(
            CommonLanguageManager.Instance.multiplayer_terracottaInvalidRoomCode.CurrentValue());
    }

    public static void ValidatePublicNodes(IReadOnlyList<string> nodes)
    {
        foreach (var node in nodes)
        {
            if (!Uri.TryCreate(node, UriKind.Absolute, out var uri) ||
                !PublicNodeSchemes.Contains(uri.Scheme) ||
                string.IsNullOrEmpty(uri.Host))
                throw new ArgumentException(string.Format(
                    CommonLanguageManager.Instance.multiplayer_terracottaInvalidNode.CurrentValue(), node));
        }
    }

    public List<string> GetConfiguredPublicNodes()
    {
        var raw = Data.ConfigEntry.TerracottaPublicNodes ?? string.Empty;
        var nodes = raw
            .Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        ValidatePublicNodes(nodes);
        return nodes;
    }

    public string GetConfiguredPublicNodesText()
    {
        return Data.ConfigEntry.TerracottaPublicNodes ?? string.Empty;
    }

    public void SaveConfiguredPublicNodes(string text)
    {
        var nodes = text
            .Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        ValidatePublicNodes(nodes);
        Data.ConfigEntry.TerracottaPublicNodes = text;
    }

    private void ResetStateInternal()
    {
        lock (_stateLock)
        {
            _state = new TerracottaState
            {
                BinaryInstalled = IsBinaryInstalled(),
                InstalledVersion = GetInstalledVersion()
            };
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
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
            Logger.Debug($"[Terracotta] Failed to kill process: {exception.Message}");
        }
    }

    private sealed class TerracottaApiState
    {
        public string State { get; set; } = string.Empty;
        public uint Index { get; set; }
        public string? Room { get; set; }
        public int? ProfileIndex { get; set; }
        public List<TerracottaApiProfile>? Profiles { get; set; }
        public string? Url { get; set; }
        public int? Type { get; set; }
        public string? Difficulty { get; set; }
    }

    private sealed class TerracottaApiProfile
    {
        public string MachineId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Vendor { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
    }
}

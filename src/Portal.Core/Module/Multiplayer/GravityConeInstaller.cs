using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Portal.Core.Const;
using Portal.Core.Module.Update;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Module.Multiplayer;

public enum ComponentUpdateStatus
{
    Current,

    UpdateRequired,

    Unknown
}

public static class GravityConeInstaller
{
    public const string ManifestUrl = "https://portal.tiouo.cc/gc.json";
    public const string GravityConeVersion = "0.1.4-alpha";
    public const string EasyTierVersion = "2.6.4";

    private const int DownloadConcurrency = 4;
    private const int BufferSize = 128 * 1024;

    private static readonly Regex VersionPattern = new(
        @"^(\d+)\.(\d+)\.(\d+)(?:-([A-Za-z0-9][A-Za-z0-9.\-]*))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static string Root => Path.Combine(ConfigPath.UserDataRootPath, "Multiplayer");
    private static string InstallationStatePath => Path.Combine(Root, "installed.json");

    public static GravityConeInstallation? FindInstalled()
    {
        var rid = GetRid();
        var state = ReadInstallationState();
        var gravityConeVersion = state is { Rid: var stateRid } && stateRid == rid
            ? state.GravityConeVersion
            : GravityConeVersion;
        var easyTierVersion = state is { Rid: var easyTierRid } && easyTierRid == rid
            ? state.EasyTierVersion
            : EasyTierVersion;
        var cliName = state is { Rid: var executableRid } && executableRid == rid
            ? state.CliExecutable
            : GetCliName(rid);
        var cli = Path.Combine(Root, "GravityCone", gravityConeVersion, cliName);
        var easyTier = Path.Combine(Root, "EasyTier", easyTierVersion, rid);
        var suffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        return File.Exists(cli) &&
               File.Exists(Path.Combine(easyTier, "easytier-core" + suffix)) &&
               File.Exists(Path.Combine(easyTier, "easytier-cli" + suffix))
            ? new GravityConeInstallation(cli, easyTier)
            : null;
    }

    public static async Task<GravityConeInstallation> EnsureInstalledAsync(
        IProgress<(int? Index, double? Progress, string Message, string? Detail)>? progress, CancellationToken cancellationToken,
        bool forceUpdate = false)
    {
        if (!forceUpdate && FindInstalled() is { } installed) return installed;

        progress?.Report((null, null, CommonLanguageManager.Instance.multiplayer_fetchingManifest.CurrentValue(), null));
        await using var manifestStream = await Client.GetStreamAsync(ManifestUrl, cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<GravityConeManifest>(manifestStream,
            cancellationToken: cancellationToken) ?? throw new InvalidDataException(CommonLanguageManager.Instance.multiplayer_manifestEmpty.CurrentValue());
        if (manifest.SchemaVersion != 1) throw new InvalidDataException(CommonLanguageManager.Instance.multiplayer_unsupportedManifestVersion.CurrentValue());

        var rid = GetRid();
        if (!manifest.GravityCone.Packages.TryGetValue(rid, out var gcPackage) ||
            !manifest.EasyTier.Packages.TryGetValue(rid, out var etPackage))
            throw new PlatformNotSupportedException(string.Format(CommonLanguageManager.Instance.multiplayer_unsupportedRid.CurrentValue(), rid));

        var gcDirectory = Path.Combine(Root, "GravityCone", manifest.GravityCone.Version);
        var etDirectory = Path.Combine(Root, "EasyTier", manifest.EasyTier.Version, rid);
        Directory.CreateDirectory(gcDirectory);
        Directory.CreateDirectory(etDirectory);

        var progressState = new ParallelDownloadProgress(2, progress);

        var gcArchive = new DownloadContext(gcPackage, gcDirectory, false, 0) { ProgressState = progressState };
        var etArchive = new DownloadContext(etPackage, etDirectory, true, 1) { ProgressState = progressState };

        progressState.Start();

        var gcTask = InstallPackageInternalAsync(gcArchive, cancellationToken);
        var etTask = InstallPackageInternalAsync(etArchive, cancellationToken);

        await Task.WhenAll(gcTask, etTask);

        progress?.Report((null, null, CommonLanguageManager.Instance.multiplayer_verifyingComponents.CurrentValue(), null));

        var cliName = gcPackage.Executable ?? GetCliName(rid);
        var cliPath = Path.Combine(gcDirectory, cliName);
        var suffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        if (!File.Exists(cliPath) || !File.Exists(Path.Combine(etDirectory, "easytier-core" + suffix)) ||
            !File.Exists(Path.Combine(etDirectory, "easytier-cli" + suffix)))
            throw new InvalidDataException(CommonLanguageManager.Instance.multiplayer_archiveMissingFiles.CurrentValue());

        MakeExecutable(cliPath);
        MakeExecutable(Path.Combine(etDirectory, "easytier-core" + suffix));
        MakeExecutable(Path.Combine(etDirectory, "easytier-cli" + suffix));
        Directory.CreateDirectory(Root);
        await File.WriteAllTextAsync(InstallationStatePath, JsonSerializer.Serialize(new InstallationState(
            manifest.GravityCone.Version, manifest.EasyTier.Version, rid, cliName)), cancellationToken);
        progress?.Report((null, 1, CommonLanguageManager.Instance.multiplayer_installComplete.CurrentValue(), null));
        CleanupOldGravityConeVersions(manifest.GravityCone.Version);
        return new GravityConeInstallation(cliPath, etDirectory);
    }

    public static string? GetInstalledGravityConeVersion()
    {
        var rid = GetRid();
        var state = ReadInstallationState();
        return state is { Rid: var stateRid } && stateRid == rid ? state.GravityConeVersion : null;
    }

    public static async Task<ComponentUpdateStatus> GetUpdateStatusAsync(CancellationToken cancellationToken)
    {
        var installedVersion = GetInstalledGravityConeVersion();
        var latestVersion = await FetchLatestGravityConeVersionAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(installedVersion) || string.IsNullOrWhiteSpace(latestVersion))
            return ComponentUpdateStatus.Unknown;
        return CompareGravityConeVersions(installedVersion, latestVersion) == 0
            ? ComponentUpdateStatus.Current
            : ComponentUpdateStatus.UpdateRequired;
    }

    private static async Task<string?> FetchLatestGravityConeVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var manifestStream = await Client.GetStreamAsync(ManifestUrl, cancellationToken);
            var manifest = await JsonSerializer.DeserializeAsync<GravityConeManifest>(manifestStream,
                cancellationToken: cancellationToken);
            if (manifest is null || manifest.SchemaVersion != 1) return null;
            return string.IsNullOrWhiteSpace(manifest.GravityCone.Version) ? null : manifest.GravityCone.Version;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException or IOException)
        {
            Logger.Warning(string.Format(LogLanguageManager.Instance.multiplayer_fetchManifestVersionFailed.CurrentValue(), Environment.NewLine, ex));
            return null;
        }
    }

    private static int CompareGravityConeVersions(string x, string y)
    {
        var xMatch = VersionPattern.Match(x);
        var yMatch = VersionPattern.Match(y);
        if (!xMatch.Success || !yMatch.Success) return string.CompareOrdinal(x, y);

        for (var i = 1; i <= 3; i++)
        {
            var difference = int.Parse(xMatch.Groups[i].Value).CompareTo(int.Parse(yMatch.Groups[i].Value));
            if (difference != 0) return difference;
        }

        var xPre = xMatch.Groups[4].Value;
        var yPre = yMatch.Groups[4].Value;
        if (xPre == yPre) return 0;
        if (xPre.Length == 0) return 1;
        if (yPre.Length == 0) return -1;
        return string.CompareOrdinal(xPre, yPre);
    }

    private static void CleanupOldGravityConeVersions(string currentVersion)
    {
        var gravityRoot = Path.Combine(Root, "GravityCone");
        if (!Directory.Exists(gravityRoot)) return;

        foreach (var directory in Directory.EnumerateDirectories(gravityRoot))
        {
            if (Path.GetFileName(directory).Equals(currentVersion, StringComparison.Ordinal)) continue;
            try
            {
                Directory.Delete(directory, true);
            }
            catch (Exception ex)
            {
                Logger.Warning(string.Format(LogLanguageManager.Instance.multiplayer_cleanupOldVersionFailed.CurrentValue(), directory, Environment.NewLine + ex));
            }
        }
    }

    private static InstallationState? ReadInstallationState()
    {
        if (!File.Exists(InstallationStatePath)) return null;
        try
        {
            return JsonSerializer.Deserialize<InstallationState>(File.ReadAllText(InstallationStatePath));
        }
        catch
        {
            return null;
        }
    }

    private static async Task InstallPackageInternalAsync(DownloadContext context, CancellationToken cancellationToken)
    {
        var package = context.Package;
        var tempRoot = Path.Combine(ConfigPath.TempFolderPath, "Multiplayer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var archive = Path.Combine(tempRoot, package.FileName);
        var extracted = Path.Combine(tempRoot, "extracted");
        Directory.CreateDirectory(extracted);
        try
        {
            var downloadUrl = GithubMirror.Apply(package.Url);
            var total = package.Size;

            context.ReportDownloadProgress(0, total);

            var supportsRange = total > 0 && await ValidateRangeSupportAsync(downloadUrl, cancellationToken);

            if (supportsRange)
                await DownloadMultiPartAsync(downloadUrl, archive, total, context, cancellationToken);
            else
                await DownloadSinglePartAsync(downloadUrl, archive, total, context, cancellationToken);

            if (package.Size > 0 && new FileInfo(archive).Length != package.Size)
                throw new InvalidDataException(string.Format(CommonLanguageManager.Instance.multiplayer_sizeVerificationFailed.CurrentValue(), package.FileName));
            await using (var stream = File.OpenRead(archive))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!hash.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(string.Format(CommonLanguageManager.Instance.multiplayer_sha256VerificationFailed.CurrentValue(), package.FileName));
            }

            context.ReportMessage(CommonLanguageManager.Instance.multiplayer_extracting.CurrentValue());
            if (package.ArchiveType.Equals("zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(archive, extracted);
            }
            else if (package.ArchiveType.Equals("tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                await using var input = File.OpenRead(archive);
                await using var gzip = new GZipStream(input, CompressionMode.Decompress);
                TarFile.ExtractToDirectory(gzip, extracted, false);
            }
            else
            {
                throw new InvalidDataException(string.Format(CommonLanguageManager.Instance.multiplayer_unsupportedArchiveFormat.CurrentValue(), package.ArchiveType));
            }

            foreach (var file in Directory.EnumerateFiles(extracted, "*", SearchOption.AllDirectories))
            {
                var relative = context.Flatten ? Path.GetFileName(file) : Path.GetRelativePath(extracted, file);
                var target = Path.Combine(context.Destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
            }

            context.MarkCompleted();
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    private static async Task<bool> ValidateRangeSupportAsync(string url, CancellationToken cancellationToken)
    {
        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, url);
        rangeRequest.Headers.Range = new RangeHeaderValue(0, 0);
        using var response =
            await Client.SendAsync(rangeRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return response.StatusCode == HttpStatusCode.PartialContent;
    }

    private static async Task DownloadMultiPartAsync(string url, string path, long total,
        DownloadContext context, CancellationToken cancellationToken)
    {
        await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Write, BufferSize,
                         true))
        {
            file.SetLength(total);
        }

        long downloaded = 0;
        var segmentSize = (total + DownloadConcurrency - 1) / DownloadConcurrency;
        var downloads = Enumerable.Range(0, DownloadConcurrency).Select(async index =>
        {
            var start = index * segmentSize;
            if (start >= total) return;
            var end = Math.Min(start + segmentSize, total) - 1;
            await DownloadRangeAsync(url, path, start, end, bytes =>
            {
                var current = Interlocked.Add(ref downloaded, bytes);
                context.ReportDownloadProgress(current, total);
            }, cancellationToken);
        });
        await Task.WhenAll(downloads);
    }

    private static async Task DownloadRangeAsync(string url, string path, long start, long end,
        Action<int> onBytes, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(start, end);
        using var response =
            await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output =
            new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Write, BufferSize, true);
        output.Seek(start, SeekOrigin.Begin);
        var buffer = new byte[BufferSize];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            onBytes(read);
        }
    }

    private static async Task DownloadSinglePartAsync(string url, string path, long total,
        DownloadContext context, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(path);
        var buffer = new byte[BufferSize];
        long downloaded = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloaded += read;
            context.ReportDownloadProgress(downloaded, total);
        }
    }

    private static string GetRid()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException(CommonLanguageManager.Instance.multiplayer_onlyX64Arm64.CurrentValue())
        };
        var os = OperatingSystem.IsWindows() ? "win" :
            OperatingSystem.IsLinux() ? "linux" :
            OperatingSystem.IsMacOS() ? "osx" : throw new PlatformNotSupportedException(CommonLanguageManager.Instance.multiplayer_osNotSupported.CurrentValue());
        return $"{os}-{architecture}";
    }

    private static string GetCliName(string rid)
    {
        return rid switch
        {
            "win-x64" => "gravitycone-cli-windows-amd64.exe",
            "win-arm64" => "gravitycone-cli-windows-arm64.exe",
            "linux-x64" => "gravitycone-cli-linux-amd64",
            "linux-arm64" => "gravitycone-cli-linux-arm64",
            "osx-x64" => "gravitycone-cli-darwin-amd64",
            "osx-arm64" => "gravitycone-cli-darwin-arm64",
            _ => throw new PlatformNotSupportedException(string.Format(CommonLanguageManager.Instance.multiplayer_unsupportedRid.CurrentValue(), rid))
        };
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, File.GetUnixFileMode(path) |
                                   UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    private sealed record InstallationState(
        string GravityConeVersion,
        string EasyTierVersion,
        string Rid,
        string CliExecutable);

    private sealed class DownloadContext
    {
        public DownloadContext(OnlinePackageManifest package, string destination, bool flatten, int index)
        {
            Package = package;
            Destination = destination;
            Flatten = flatten;
            Index = index;
        }

        public OnlinePackageManifest Package { get; }
        public string Destination { get; }
        public bool Flatten { get; }
        public int Index { get; }

        public ParallelDownloadProgress? ProgressState { get; set; }

        public void ReportDownloadProgress(long downloaded, long total)
        {
            ProgressState?.ReportDownloadProgress(Index, downloaded, total, ComponentName);
        }

        public void ReportMessage(string message)
        {
            ProgressState?.ReportMessage(Index, message);
        }

        public void MarkCompleted()
        {
            ProgressState?.MarkCompleted(Index);
        }

        public string ComponentName => Index == 0 ? "GravityCone" : "EasyTier";
    }

    private sealed class ParallelDownloadProgress
    {
        private const int StateIdle = 0;
        private const int StateDownloading = 1;
        private const int StateExtracting = 2;
        private const int StateCompleted = 3;
        private readonly int _count;
        private readonly long[] _downloadedBytes;
        private readonly string[] _messages;
        private readonly IProgress<(int? Index, double? Progress, string Message, string? Detail)>? _progress;
        private readonly DateTime[] _lastReport;
        private readonly long[] _lastBytes;
        private readonly long[] _speeds;
        private readonly int[] _states;
        private readonly long[] _totalBytes;
        private int _started;

        public ParallelDownloadProgress(int count, IProgress<(int? Index, double? Progress, string Message, string? Detail)>? progress)
        {
            _count = count;
            _progress = progress;
            _downloadedBytes = new long[count];
            _totalBytes = new long[count];
            _messages = new string[count];
            _states = new int[count];
            _lastReport = new DateTime[count];
            _lastBytes = new long[count];
            _speeds = new long[count];
        }

        public void Start()
        {
            Interlocked.Exchange(ref _started, 1);
            Report();
        }

        public void ReportDownloadProgress(int index, long downloaded, long total, string component)
        {
            Interlocked.Exchange(ref _downloadedBytes[index], downloaded);
            Interlocked.Exchange(ref _totalBytes[index], total);
            _messages[index] = string.Format(CommonLanguageManager.Instance.multiplayer_downloading.CurrentValue(), component);
            var now = DateTime.UtcNow;
            var elapsed = now - _lastReport[index];
            var speed = elapsed.TotalSeconds > 0 ? (long)Math.Max(0, (downloaded - _lastBytes[index]) / elapsed.TotalSeconds) : 0;
            _lastReport[index] = now;
            _lastBytes[index] = downloaded;
            _speeds[index] = speed;
            Interlocked.CompareExchange(ref _states[index], StateDownloading, StateIdle);
            Report();
        }

        public void ReportMessage(int index, string message)
        {
            _messages[index] = message;
            Interlocked.Exchange(ref _states[index], StateExtracting);
            Report();
        }

        public void MarkCompleted(int index)
        {
            Interlocked.Exchange(ref _states[index], StateCompleted);
            Report();
        }

        private void Report()
        {
            if (Volatile.Read(ref _started) == 0) return;

            long totalSum = 0;
            long downloadedSum = 0;
            var completedCount = 0;

            for (var i = 0; i < _count; i++)
            {
                var total = Volatile.Read(ref _totalBytes[i]);
                var downloaded = Volatile.Read(ref _downloadedBytes[i]);
                totalSum += total;
                downloadedSum += Math.Min(downloaded, total);

                var state = Volatile.Read(ref _states[i]);
                if (state == StateCompleted) completedCount++;

                if (!string.IsNullOrEmpty(_messages[i]))
                    _progress?.Report((i, state == StateDownloading && total > 0 ? (double)downloaded / total : state == StateCompleted ? 1 : null, _messages[i], state == StateDownloading ? FormatSpeed(_speeds[i]) : null));
            }

            if (completedCount == _count)
                _progress?.Report((null, 1, CommonLanguageManager.Instance.multiplayer_downloadComplete.CurrentValue(), null));
        }

        private static string FormatSpeed(long bytesPerSecond) => string.Format(
            CommonLanguageManager.Instance.download_speed.CurrentValue(), bytesPerSecond switch
        {
            >= 1024 * 1024 => $"{bytesPerSecond / 1024d / 1024d:0.0} MB/s",
            >= 1024 => $"{bytesPerSecond / 1024d:0.0} KB/s",
            _ => $"{bytesPerSecond} B/s"
        });
    }
}

using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Portal.Const;
using Portal.Module.Update;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Module.Multiplayer;

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
        IProgress<(double? Progress, string Message)>? progress, CancellationToken cancellationToken,
        bool forceUpdate = false)
    {
        if (!forceUpdate && FindInstalled() is { } installed) return installed;

        progress?.Report((null, "正在获取联机组件清单"));
        await using var manifestStream = await Client.GetStreamAsync(ManifestUrl, cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<GravityConeManifest>(manifestStream,
            cancellationToken: cancellationToken) ?? throw new InvalidDataException("联机组件清单为空。");
        if (manifest.SchemaVersion != 1) throw new InvalidDataException("不支持的联机组件清单版本。");

        var rid = GetRid();
        if (!manifest.GravityCone.Packages.TryGetValue(rid, out var gcPackage) ||
            !manifest.EasyTier.Packages.TryGetValue(rid, out var etPackage))
            throw new PlatformNotSupportedException($"联机组件暂不支持 {rid}。");

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

        progress?.Report((null, "正在校验联机组件"));

        var cliName = gcPackage.Executable ?? GetCliName(rid);
        var cliPath = Path.Combine(gcDirectory, cliName);
        var suffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        if (!File.Exists(cliPath) || !File.Exists(Path.Combine(etDirectory, "easytier-core" + suffix)) ||
            !File.Exists(Path.Combine(etDirectory, "easytier-cli" + suffix)))
            throw new InvalidDataException("联机组件压缩包缺少必要文件。");

        MakeExecutable(cliPath);
        MakeExecutable(Path.Combine(etDirectory, "easytier-core" + suffix));
        MakeExecutable(Path.Combine(etDirectory, "easytier-cli" + suffix));
        Directory.CreateDirectory(Root);
        await File.WriteAllTextAsync(InstallationStatePath, JsonSerializer.Serialize(new InstallationState(
            manifest.GravityCone.Version, manifest.EasyTier.Version, rid, cliName)), cancellationToken);
        progress?.Report((1, "联机组件安装完成"));
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
            Logger.Warning($"获取联机组件清单版本失败。{Environment.NewLine}{ex}");
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
                Logger.Warning($"清理旧版联机组件失败：{directory}{Environment.NewLine}{ex}");
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

            context.ReportDownloadProgress(0, total, $"正在下载 {package.FileName}");

            bool supportsRange = total > 0 && await ValidateRangeSupportAsync(downloadUrl, cancellationToken);

            if (supportsRange)
                await DownloadMultiPartAsync(downloadUrl, archive, total, context, cancellationToken);
            else
                await DownloadSinglePartAsync(downloadUrl, archive, total, context, cancellationToken);

            if (package.Size > 0 && new FileInfo(archive).Length != package.Size)
                throw new InvalidDataException($"{package.FileName} 文件大小校验失败。");
            await using (var stream = File.OpenRead(archive))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!hash.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{package.FileName} SHA-256 校验失败。");
            }

            context.ReportMessage($"正在解压 {package.FileName}");
            if (package.ArchiveType.Equals("zip", StringComparison.OrdinalIgnoreCase))
                ZipFile.ExtractToDirectory(archive, extracted);
            else if (package.ArchiveType.Equals("tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                await using var input = File.OpenRead(archive);
                await using var gzip = new GZipStream(input, CompressionMode.Decompress);
                TarFile.ExtractToDirectory(gzip, extracted, false);
            }
            else throw new InvalidDataException($"不支持的压缩格式：{package.ArchiveType}");

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
        using var response = await Client.SendAsync(rangeRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return response.StatusCode == System.Net.HttpStatusCode.PartialContent;
    }

    private static async Task DownloadMultiPartAsync(string url, string path, long total,
        DownloadContext context, CancellationToken cancellationToken)
    {
        await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Write, BufferSize, true))
            file.SetLength(total);

        long downloaded = 0;
        long segmentSize = (total + DownloadConcurrency - 1) / DownloadConcurrency;
        var downloads = Enumerable.Range(0, DownloadConcurrency).Select(async index =>
        {
            var start = index * segmentSize;
            if (start >= total) return;
            var end = Math.Min(start + segmentSize, total) - 1;
            await DownloadRangeAsync(url, path, start, end, bytes =>
            {
                long current = Interlocked.Add(ref downloaded, bytes);
                context.ReportDownloadProgress(current, total, $"正在下载 {context.Package.FileName}");
            }, cancellationToken);
        });
        await Task.WhenAll(downloads);
    }

    private static async Task DownloadRangeAsync(string url, string path, long start, long end,
        Action<int> onBytes, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(start, end);
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Write, BufferSize, true);
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
            context.ReportDownloadProgress(downloaded, total, $"正在下载 {context.Package.FileName}");
        }
    }

    private static string GetRid()
    {
        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException("联机组件仅支持 x64 和 arm64。")
        };
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" :
            OperatingSystem.IsMacOS() ? "osx" : throw new PlatformNotSupportedException("当前系统不支持联机组件。");
        return $"{os}-{architecture}";
    }

    private static string GetCliName(string rid) => rid switch
    {
        "win-x64" => "gravitycone-cli-windows-amd64.exe",
        "win-arm64" => "gravitycone-cli-windows-arm64.exe",
        "linux-x64" => "gravitycone-cli-linux-amd64",
        "linux-arm64" => "gravitycone-cli-linux-arm64",
        "osx-x64" => "gravitycone-cli-darwin-amd64",
        "osx-arm64" => "gravitycone-cli-darwin-arm64",
        _ => throw new PlatformNotSupportedException($"联机组件暂不支持 {rid}。")
    };

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, File.GetUnixFileMode(path) |
                                  UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    private sealed record InstallationState(string GravityConeVersion, string EasyTierVersion, string Rid,
        string CliExecutable);

    private sealed class DownloadContext
    {
        public OnlinePackageManifest Package { get; }
        public string Destination { get; }
        public bool Flatten { get; }
        public int Index { get; }

        public ParallelDownloadProgress? ProgressState { get; set; }

        public DownloadContext(OnlinePackageManifest package, string destination, bool flatten, int index)
        {
            Package = package;
            Destination = destination;
            Flatten = flatten;
            Index = index;
        }

        public void ReportDownloadProgress(long downloaded, long total, string message)
        {
            ProgressState?.ReportDownloadProgress(Index, downloaded, total, message);
        }

        public void ReportMessage(string message)
        {
            ProgressState?.ReportMessage(Index, message);
        }

        public void MarkCompleted()
        {
            ProgressState?.MarkCompleted(Index);
        }
    }

    private sealed class ParallelDownloadProgress
    {
        private readonly int _count;
        private readonly IProgress<(double? Progress, string Message)>? _progress;
        private readonly long[] _downloadedBytes;
        private readonly long[] _totalBytes;
        private readonly string[] _messages;
        private readonly int[] _states;
        private int _started;

        private const int StateIdle = 0;
        private const int StateDownloading = 1;
        private const int StateExtracting = 2;
        private const int StateCompleted = 3;

        public ParallelDownloadProgress(int count, IProgress<(double? Progress, string Message)>? progress)
        {
            _count = count;
            _progress = progress;
            _downloadedBytes = new long[count];
            _totalBytes = new long[count];
            _messages = new string[count];
            _states = new int[count];
        }

        public void Start()
        {
            Interlocked.Exchange(ref _started, 1);
            Report();
        }

        public void ReportDownloadProgress(int index, long downloaded, long total, string message)
        {
            Interlocked.Exchange(ref _downloadedBytes[index], downloaded);
            Interlocked.Exchange(ref _totalBytes[index], total);
            _messages[index] = message;
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
            var messages = new List<string>();
            int completedCount = 0;

            for (int i = 0; i < _count; i++)
            {
                long total = Volatile.Read(ref _totalBytes[i]);
                long downloaded = Volatile.Read(ref _downloadedBytes[i]);
                totalSum += total;
                downloadedSum += Math.Min(downloaded, total);

                var state = Volatile.Read(ref _states[i]);
                if (state == StateCompleted) completedCount++;

                if (!string.IsNullOrEmpty(_messages[i]))
                    messages.Add(_messages[i]);
            }

            double? progress = totalSum > 0 ? (double)downloadedSum / totalSum : null;
            string message = completedCount == _count
                ? "联机组件下载完成"
                : messages.Count > 0
                    ? string.Join("，", messages)
                    : "正在下载联机组件";

            _progress?.Report((progress, message));
        }
    }
}
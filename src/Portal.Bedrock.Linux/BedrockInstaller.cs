using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Portal.Bedrock.Core;
using Portal.Bedrock.Standard.Interface;
using Portal.Localization;

namespace Portal.Bedrock.Linux;

public sealed class BedrockInstaller : IBedrockInstaller
{
    private const string VersionDatabaseUrl = "https://data.mcappx.com/v2/bedrock.json";
    private const int DownloadBufferSize = 1024 * 256;
    private static readonly SemaphoreSlim VersionLoadLock = new(1, 1);
    private static readonly object DownloadClientLock = new();
    private static HttpClient? _downloadClient;
    private static int _downloadClientConfigurationVersion = -1;
    private static IReadOnlyList<BedrockGdkVersion>? _cachedVersions;

    public async Task<IReadOnlyList<BedrockGdkVersion>> GetGdkVersionsAsync(bool refresh,
        CancellationToken cancellationToken)
    {
        LinuxBedrockRuntimeResolver.EnsureSupportedPlatform();
        if (!refresh && _cachedVersions is not null) return _cachedVersions;

        Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockInstall_loadingGdkVersions.CurrentValue(), refresh));
        await VersionLoadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!refresh && _cachedVersions is not null) return _cachedVersions;
            var database = await VersionsHelper.GetBuildDatabaseAsync(VersionDatabaseUrl, cancellationToken)
                           ?? throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockInstall_noVersionData.CurrentValue());
            var versions = new List<BedrockGdkVersion>();

            await foreach (var (_, build) in database.Builds.WithCancellation(cancellationToken))
            {
                if (!IsSupportedBuild(build) || string.IsNullOrWhiteSpace(build.ID)) continue;
                versions.Add(new BedrockGdkVersion(build.ID, ParseReleaseTime(build.Date),
                    build.Type == MinecraftGameTypeVersion.Preview));
            }

            _cachedVersions = versions.OrderByDescending(version => version.ReleaseTime)
                .ThenByDescending(version => ParseVersion(version.Id))
                .ThenByDescending(version => version.Id, StringComparer.Ordinal)
                .ToList();
            Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockInstall_gdkVersionsLoaded.CurrentValue(), _cachedVersions.Count));
            return _cachedVersions;
        }
        finally
        {
            VersionLoadLock.Release();
        }
    }

    public async Task InstallGdkAsync(BedrockOnlineInstallRequest request,
        IProgress<BedrockInstallProgress>? progress = null)
    {
        LinuxBedrockRuntimeResolver.EnsureSupportedPlatform();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationPath);

        var destination = Path.GetFullPath(request.DestinationPath);
        if (Directory.Exists(destination)) throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockInstall_destinationExists.CurrentValue());

        Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockInstall_installGdkStart.CurrentValue(), request.Version.Id, destination));
        var build = await FindBuildAsync(request.Version, request.CancellationToken).ConfigureAwait(false);
        var variation = GetX64Variation(build);
        if (string.IsNullOrWhiteSpace(variation.MD5))
            throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockInstall_gdkMissingMd5.CurrentValue());

        var core = new BedrockCore();
        var packageUrl = await core.GetPackageUri(build, Architecture.X64).ConfigureAwait(false);
        var packagePath = GetPackagePath(request.Version.Id);
        await DownloadPackageAsync(packageUrl, packagePath, variation.MD5, progress, request.CancellationToken)
            .ConfigureAwait(false);

        request.CancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new BedrockInstallProgress(0, 0, string.Empty, InstallStates.Extracting.ToString()));
        var installed = await core.InstallPackageAsync(new LocalGamePackageOptions
        {
            FileFullPath = packagePath,
            InstallDstFolder = destination,
            Type = MinecraftBuildTypeVersion.GDK,
            GameTypeVersion = request.Version.IsPreview
                ? MinecraftGameTypeVersion.Preview
                : MinecraftGameTypeVersion.Release,
            CancellationToken = request.CancellationToken,
            InstallStates = new Progress<InstallStates>(state =>
                progress?.Report(new BedrockInstallProgress(0, 0, string.Empty, state.ToString()))),
            ExtractionProgress = new Progress<DecompressProgress>(extraction =>
                progress?.Report(new BedrockInstallProgress(extraction.CurrentCount, extraction.TotalCount,
                    extraction.FileName, InstallStates.Extracting.ToString())))
        }).ConfigureAwait(false);

        if (installed is null || !File.Exists(Path.Combine(destination, "Minecraft.Windows.exe")))
            throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockInstall_gdkInstanceInvalid.CurrentValue());
        Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockInstall_installGdkComplete.CurrentValue(), request.Version.Id, destination));
    }

    private static bool IsSupportedBuild(BuildInfo build) =>
        build.Type is MinecraftGameTypeVersion.Release or MinecraftGameTypeVersion.Preview &&
        build.BuildType == MinecraftBuildTypeVersion.GDK &&
        build.Variations.Any(variation => variation.Arch == Architecture.X64 && variation.MetaData.Count > 0);

    private static Variation GetX64Variation(BuildInfo build) => build.Variations.First(variation =>
        variation.Arch == Architecture.X64 && variation.MetaData.Count > 0);

    private static async Task<BuildInfo> FindBuildAsync(BedrockGdkVersion version,
        CancellationToken cancellationToken)
    {
        var database = await VersionsHelper.GetBuildDatabaseAsync(VersionDatabaseUrl, cancellationToken)
                       ?? throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockInstall_noVersionData.CurrentValue());
        var type = version.IsPreview ? MinecraftGameTypeVersion.Preview : MinecraftGameTypeVersion.Release;
        await foreach (var (_, build) in database.Builds.WithCancellation(cancellationToken))
        {
            if (build.ID == version.Id && build.Type == type && IsSupportedBuild(build)) return build;
        }

        throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockInstall_gdkVersionUnavailable.CurrentValue());
    }

    private static async Task DownloadPackageAsync(string url, string packagePath, string expectedMd5,
        IProgress<BedrockInstallProgress>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        if (File.Exists(packagePath) && await MatchesMd5Async(packagePath, expectedMd5, cancellationToken))
        {
            Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockInstall_usingValidatedCache.CurrentValue(), packagePath));
            progress?.Report(new BedrockInstallProgress(1, 1, Path.GetFileName(packagePath), "Using cached package"));
            return;
        }

        if (File.Exists(packagePath))
        {
            Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockInstall_deletingInvalidCache.CurrentValue(), packagePath));
            File.Delete(packagePath);
        }
        var temporaryPath = packagePath + ".download";
        if (File.Exists(temporaryPath)) File.Delete(temporaryPath);

        try
        {
            Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockInstall_downloadingPackage.CurrentValue(), url, temporaryPath));
            using var response = await GetDownloadClient().GetAsync(url, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                DownloadBufferSize, FileOptions.Asynchronous);
            var buffer = new byte[DownloadBufferSize];
            var stopwatch = Stopwatch.StartNew();
            long downloaded = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                downloaded += read;
                var speed = stopwatch.Elapsed.TotalSeconds > 0 ? downloaded / stopwatch.Elapsed.TotalSeconds : 0;
                TimeSpan? remaining = speed > 0 && total > 0
                    ? TimeSpan.FromSeconds(Math.Max(0, total - downloaded) / speed)
                    : null;
                progress?.Report(new BedrockInstallProgress(downloaded, total, Path.GetFileName(packagePath),
                    "Downloading", speed, remaining));
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Close();
            if (!await MatchesMd5Async(temporaryPath, expectedMd5, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException(CommonLanguageManager.Instance.bedrockInstall_md5VerificationFailed.CurrentValue());

            File.Move(temporaryPath, packagePath);
            Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockInstall_packageDownloaded.CurrentValue(), packagePath, downloaded));
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockInstall_cleaningTempFile.CurrentValue(), temporaryPath));
                File.Delete(temporaryPath);
            }
        }
    }

    private static string GetPackagePath(string versionId)
    {
        var cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrWhiteSpace(cacheHome))
            cacheHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        return Path.Combine(cacheHome, "Portal", "Bedrock", $"{versionId}.insPack");
    }

    private static HttpClient GetDownloadClient()
    {
        lock (DownloadClientLock)
        {
            if (_downloadClient is not null &&
                _downloadClientConfigurationVersion == BedrockNetworkConfiguration.Version)
                return _downloadClient;

            _downloadClient?.Dispose();
            var proxyServer = BedrockNetworkConfiguration.ProxyServer;
            if (!string.IsNullOrWhiteSpace(proxyServer) &&
                !proxyServer.Contains("://", StringComparison.Ordinal))
                proxyServer = $"http://{proxyServer}";
            var hasProxy = Uri.TryCreate(proxyServer, UriKind.Absolute, out var proxyUri);
            var handler = new SocketsHttpHandler
            {
                UseProxy = !BedrockNetworkConfiguration.DisableSystemProxy || hasProxy,
                Proxy = hasProxy ? new WebProxy(proxyUri) : null,
                AllowAutoRedirect = true,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };
            _downloadClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            _downloadClient.DefaultRequestHeaders.UserAgent.ParseAdd(BedrockNetworkConfiguration.UserAgent);
            _downloadClientConfigurationVersion = BedrockNetworkConfiguration.Version;
            Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockInstall_downloadClientCreated.CurrentValue(), (hasProxy ? proxyUri : null), !BedrockNetworkConfiguration.DisableSystemProxy));
            return _downloadClient;
        }
    }

    private static async Task<bool> MatchesMd5Async(string path, string expectedMd5,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await MD5.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(Convert.ToHexString(hash), expectedMd5, StringComparison.OrdinalIgnoreCase);
    }

    private static Version ParseVersion(string version) =>
        Version.TryParse(version, out var parsed) ? parsed : new Version();

    private static DateTime ParseReleaseTime(string date) =>
        DateTime.TryParse(date, out var parsed) ? parsed : DateTime.MinValue;
}

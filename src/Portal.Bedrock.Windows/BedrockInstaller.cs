using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Portal.Bedrock.Core;
using Portal.Bedrock.Core.Windows;
using Portal.Bedrock.Standard.Interface;
using Portal.Bedrock.Standard.Manifest;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using Portal.Localization;

namespace Portal.Bedrock;

public sealed class BedrockInstaller : IBedrockInstaller
{
    private const string VersionDatabaseUrl = "https://data.mcappx.com/v2/bedrock.json";
    private const int DownloadConcurrency = 8;
    private const int DownloadBufferSize = 1024 * 256;
    private const int SourceProbeBytes = 1024 * 1024;
    private const int RetainedPackageCount = 2;
    private static readonly SemaphoreSlim VersionLoadLock = new(1, 1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PackageLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> ActivePackages =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly object PackageCleanupLock = new();
    private static readonly object DownloadClientLock = new();
    private static HttpClient? _downloadClient;
    private static int _downloadClientConfigurationVersion = -1;
    private static IReadOnlyList<BedrockVersion>? _cachedVersions;

    public async Task<IReadOnlyList<BedrockGdkVersion>> GetGdkVersionsAsync(bool refresh,
        CancellationToken cancellationToken) => (await GetVersionsAsync(refresh, cancellationToken))
        .Where(version => version.BuildType == BedrockBuildType.GDK)
        .Select(version => new BedrockGdkVersion(version.Id, version.ReleaseTime, version.IsPreview))
        .ToList();

    public async Task<IReadOnlyList<BedrockVersion>> GetVersionsAsync(bool refresh,
        CancellationToken cancellationToken)
    {
        if (!refresh && _cachedVersions is not null) return _cachedVersions;

        await VersionLoadLock.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && _cachedVersions is not null) return _cachedVersions;

            var database = await VersionsHelper.GetBuildDatabaseAsync(VersionDatabaseUrl, cancellationToken)
                           ?? throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockInstall_noVersionData.CurrentValue());
            var builds = new List<BedrockVersion>();

            await foreach (var (_, build) in database.Builds.WithCancellation(cancellationToken))
            {
                if (build.Type is not (MinecraftGameTypeVersion.Release or MinecraftGameTypeVersion.Preview) ||
                    build.BuildType is not (MinecraftBuildTypeVersion.GDK or MinecraftBuildTypeVersion.UWP) ||
                    string.IsNullOrWhiteSpace(build.ID) ||
                    !build.Variations.Any(variation => variation.Arch == Architecture.X64 && variation.MetaData.Count > 0))
                    continue;

                builds.Add(new BedrockVersion(build.ID, ParseReleaseTime(build.Date),
                    build.Type == MinecraftGameTypeVersion.Preview,
                    build.BuildType == MinecraftBuildTypeVersion.GDK ? BedrockBuildType.GDK : BedrockBuildType.UWP));
            }

            return _cachedVersions = builds.OrderByDescending(version => version.ReleaseTime)
                .ThenByDescending(version => ParseVersion(version.Id))
                .ThenByDescending(version => version.Id, StringComparer.Ordinal)
                .ThenBy(version => version.BuildType)
                .ToList();
        }
        finally
        {
            VersionLoadLock.Release();
        }
    }

    public async Task InstallGdkAsync(BedrockOnlineInstallRequest request, IProgress<BedrockInstallProgress>? progress = null)
    {
        await InstallAsync(new BedrockInstallRequest(
            new BedrockVersion(request.Version.Id, request.Version.ReleaseTime, request.Version.IsPreview,
                BedrockBuildType.GDK), request.DestinationPath, request.CancellationToken), progress);
    }

    public async Task InstallAsync(BedrockInstallRequest request, IProgress<BedrockInstallProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationPath);

        var destination = Path.GetFullPath(request.DestinationPath);
        if (Directory.Exists(destination))
            throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockInstall_destinationExists.CurrentValue());

        var core = new BedrockWindowsCore();
        await core.InitAsync();
        await core.AutoCompleteGameInput();
        var build = await FindBuildAsync(request.Version, request.CancellationToken);
        var packageUrl = await core.GetPackageUri(build, Architecture.X64);
        var packagePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cc.tiouo.Portal", "Cache", "Bedrock", $"{request.Version.Id}-{request.Version.BuildLabel}.insPack");

        MarkPackageActive(packagePath);
        var packageLock = PackageLocks.GetOrAdd(packagePath, static _ => new SemaphoreSlim(1, 1));
        try
        {
            await packageLock.WaitAsync(request.CancellationToken);
            try
            {
                if (Directory.Exists(destination))
                    throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockInstall_destinationExists.CurrentValue());

                await DownloadPackageAsync(packageUrl, packagePath, build, request.Version.BuildType, progress,
                    request.CancellationToken);
                request.CancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new BedrockInstallProgress(0, 0, string.Empty, "Extracting"));
                await core.InstallPackageAsync(new LocalGamePackageOptions
                {
                    FileFullPath = packagePath,
                    InstallDstFolder = destination,
                    Type = request.Version.BuildType == BedrockBuildType.GDK
                        ? MinecraftBuildTypeVersion.GDK
                        : MinecraftBuildTypeVersion.UWP,
                    GameTypeVersion = request.Version.IsPreview
                        ? MinecraftGameTypeVersion.Preview
                        : MinecraftGameTypeVersion.Release,
                    CancellationToken = request.CancellationToken,
                    InstallStates = new Progress<InstallStates>(state =>
                        progress?.Report(new BedrockInstallProgress(0, 0, string.Empty, state.ToString()))),
                    ExtractionProgress = new Progress<DecompressProgress>(extraction =>
                        progress?.Report(new BedrockInstallProgress(
                            extraction.CurrentCount,
                            extraction.TotalCount,
                            extraction.FileName,
                            InstallStates.Extracting.ToString())))
                });
                TouchPackage(packagePath);
            }
            finally
            {
                packageLock.Release();
            }
        }
        finally
        {
            MarkPackageInactive(packagePath);
        }

        CleanupPackageCache(Path.GetDirectoryName(packagePath)!);

        try
        {
            await BedrockWindowsPrerequisites.EnsureDependenciesAsync(destination, request.Version.BuildType, null,
                null, request.CancellationToken);
        }
        catch (Exception exception)
        {
            Console.WriteLine(string.Format(CommonLanguageManager.Instance.bedrockInstall_dependencyInstallFailedIgnored.CurrentValue(), exception.Message));
        }
    }

    private static Version ParseVersion(string version) => Version.TryParse(version, out var parsed) ? parsed : new Version();
    private static DateTime ParseReleaseTime(string date) => DateTime.TryParse(date, out var parsed) ? parsed : DateTime.MinValue;

    private static async Task<BuildInfo> FindBuildAsync(BedrockVersion version, CancellationToken cancellationToken)
    {
        var database = await VersionsHelper.GetBuildDatabaseAsync(VersionDatabaseUrl, cancellationToken)
                       ?? throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockInstall_noVersionData.CurrentValue());
        var type = version.IsPreview ? MinecraftGameTypeVersion.Preview : MinecraftGameTypeVersion.Release;

        await foreach (var (_, build) in database.Builds.WithCancellation(cancellationToken))
        {
            var buildType = version.BuildType == BedrockBuildType.GDK
                ? MinecraftBuildTypeVersion.GDK
                : MinecraftBuildTypeVersion.UWP;
            if (build.ID == version.Id && build.Type == type && build.BuildType == buildType &&
                build.Variations.Any(variation => variation.Arch == Architecture.X64 && variation.MetaData.Count > 0))
                return build;
        }

        throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockInstall_versionUnavailable.CurrentValue());
    }

    private static async Task DownloadPackageAsync(string url, string packagePath, BuildInfo build,
        BedrockBuildType buildType,
        IProgress<BedrockInstallProgress>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        
        var expectedMd5 = build.Variations
            .First(variation => variation.Arch == Architecture.X64 && variation.MetaData.Count > 0).MD5;
        if (string.IsNullOrWhiteSpace(expectedMd5))
            throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockInstall_missingPackageHash.CurrentValue());
        if (File.Exists(packagePath) && await MatchesMd5Async(packagePath, expectedMd5, cancellationToken))
        {
            progress?.Report(new BedrockInstallProgress(1, 1, Path.GetFileName(packagePath), "Using cached package"));
            return;
        }

        if (File.Exists(packagePath)) File.Delete(packagePath);
        var candidates = buildType == BedrockBuildType.GDK ? GetGdkDownloadUrls(url).ToList() : [url];
        string? selected = null;
        if (buildType == BedrockBuildType.GDK)
        {
            progress?.Report(new BedrockInstallProgress(0, 0, string.Empty, "Selecting source"));
            selected = await SelectFastestSourceAsync(candidates, cancellationToken);
        }
        var orderedCandidates = selected is null
            ? candidates
            : new[] { selected }.Concat(candidates.Where(candidate => candidate != selected));

        foreach (var candidate in orderedCandidates)
        {
            try
            {
                await DownloadAsync(candidate, packagePath, progress, cancellationToken);
                if (await MatchesMd5Async(packagePath, expectedMd5, cancellationToken)) return;
                File.Delete(packagePath);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                
                if (File.Exists(packagePath)) File.Delete(packagePath);
            }
        }

        throw new InvalidOperationException(string.Format(CommonLanguageManager.Instance.bedrockInstall_cannotDownloadOrVerifyPackage.CurrentValue(), buildType));
    }

    private static IEnumerable<string> GetGdkDownloadUrls(string url)
    {
        var uri = new Uri(url);
        var path = uri.PathAndQuery;
        var sources = new[]
        {
            url,
            "http://assets1.xboxlive.cn" + path,
            "http://assets2.xboxlive.cn" + path,
            "http://assets1.xboxlive.com" + path,
            "http://assets2.xboxlive.com" + path,
            "http://xvcf1.xboxlive.com" + path,
            "http://xvcf2.xboxlive.com" + path,
            "http://d1.xboxlive.cn" + path,
            "http://d2.xboxlive.cn" + path,
            "http://d1.xboxlive.com" + path,
            "http://d2.xboxlive.com" + path
        };
        return sources.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task DownloadAsync(string url, string path, IProgress<BedrockInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var probeRequest = new HttpRequestMessage(HttpMethod.Get, url);
        probeRequest.Headers.Range = new RangeHeaderValue(0, 0);
        using var probeResponse = await GetDownloadClient().SendAsync(probeRequest, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        probeResponse.EnsureSuccessStatusCode();
        var total = probeResponse.Content.Headers.ContentRange?.Length ?? probeResponse.Content.Headers.ContentLength ?? 0;
        if (total <= 0 || probeResponse.StatusCode != HttpStatusCode.PartialContent)
        {
            await DownloadSinglePartAsync(url, path, total, progress, cancellationToken);
            return;
        }

        await DownloadMultiPartAsync(url, path, total, progress, cancellationToken);
    }

    private static async Task<string?> SelectFastestSourceAsync(IReadOnlyList<string> candidates,
        CancellationToken cancellationToken)
    {
        var probes = candidates.Select(candidate => ProbeSourceAsync(candidate, cancellationToken));
        var results = await Task.WhenAll(probes);
        return results.Where(result => result.Speed > 0)
            .OrderByDescending(result => result.Speed)
            .Select(result => result.Url)
            .FirstOrDefault();
    }

    private static async Task<(string Url, double Speed)> ProbeSourceAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, SourceProbeBytes - 1);
            var stopwatch = Stopwatch.StartNew();
            using var response = await GetDownloadClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.PartialContent) return (url, 0);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[DownloadBufferSize];
            var bytes = 0;
            while (bytes < SourceProbeBytes)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, SourceProbeBytes - bytes)), timeout.Token);
                if (read == 0) break;
                bytes += read;
            }
            stopwatch.Stop();
            return bytes == SourceProbeBytes && stopwatch.Elapsed.TotalSeconds > 0
                ? (url, bytes / stopwatch.Elapsed.TotalSeconds)
                : (url, 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            
            
            return (url, 0);
        }
    }

    private static async Task DownloadMultiPartAsync(string url, string path, long total,
        IProgress<BedrockInstallProgress>? progress, CancellationToken cancellationToken)
    {
        await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Write, DownloadBufferSize, true))
            file.SetLength(total);

        long downloaded = 0;
        var stopwatch = Stopwatch.StartNew();
        using var progressCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progressTask = ReportProgressAsync(path, total, () => Interlocked.Read(ref downloaded), stopwatch, progress,
            progressCancellation.Token);
        try
        {
            var segmentSize = (total + DownloadConcurrency - 1) / DownloadConcurrency;
            var downloads = Enumerable.Range(0, DownloadConcurrency).Select(async index =>
            {
                var start = index * segmentSize;
                if (start >= total) return;
                var end = Math.Min(start + segmentSize, total) - 1;
                await DownloadRangeAsync(url, path, start, end, bytes => Interlocked.Add(ref downloaded, bytes), cancellationToken);
            });
            await Task.WhenAll(downloads);
        }
        finally
        {
            progressCancellation.Cancel();
            await IgnoreCancellationAsync(progressTask);
        }
        progress?.Report(new BedrockInstallProgress(total, total, Path.GetFileName(path), "Downloading", 0, TimeSpan.Zero));
    }

    private static async Task DownloadRangeAsync(string url, string path, long start, long end, Action<int> onBytes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(start, end);
        using var response = await GetDownloadClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.PartialContent)
            throw new HttpRequestException(CommonLanguageManager.Instance.bedrockInstall_sourceNoRangeDownload.CurrentValue());
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Write, DownloadBufferSize, true);
        output.Seek(start, SeekOrigin.Begin);
        var buffer = new byte[DownloadBufferSize];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            onBytes(read);
        }
    }

    private static async Task DownloadSinglePartAsync(string url, string path, long total,
        IProgress<BedrockInstallProgress>? progress, CancellationToken cancellationToken)
    {
        long downloaded = 0;
        var stopwatch = Stopwatch.StartNew();
        using var progressCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progressTask = ReportProgressAsync(path, total, () => Interlocked.Read(ref downloaded), stopwatch, progress,
            progressCancellation.Token);
        try
        {
            using var response = await GetDownloadClient().GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, DownloadBufferSize, true);
            var buffer = new byte[DownloadBufferSize];
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                Interlocked.Add(ref downloaded, read);
            }
        }
        finally
        {
            progressCancellation.Cancel();
            await IgnoreCancellationAsync(progressTask);
        }
        progress?.Report(new BedrockInstallProgress(downloaded, total, Path.GetFileName(path), "Downloading", 0, TimeSpan.Zero));
    }

    private static async Task ReportProgressAsync(string path, long total, Func<long> getDownloaded, Stopwatch stopwatch,
        IProgress<BedrockInstallProgress>? progress, CancellationToken cancellationToken)
    {
        var lastBytes = 0L;
        var lastTime = 0d;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var downloaded = getDownloaded();
            var elapsed = stopwatch.Elapsed.TotalSeconds;
            var speed = elapsed > lastTime ? (downloaded - lastBytes) / (elapsed - lastTime) : 0;
            var remaining = speed > 0 ? TimeSpan.FromSeconds((total - downloaded) / speed) : TimeSpan.Zero;
            progress?.Report(new BedrockInstallProgress(downloaded, total, Path.GetFileName(path), "Downloading", speed, remaining));
            lastBytes = downloaded;
            lastTime = elapsed;
        }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
    }

    private static HttpClient GetDownloadClient()
    {
        lock (DownloadClientLock)
        {
            if (_downloadClient is not null && _downloadClientConfigurationVersion == BedrockNetworkConfiguration.Version)
                return _downloadClient;

            _downloadClient = CreateDownloadClient();
            _downloadClientConfigurationVersion = BedrockNetworkConfiguration.Version;
            return _downloadClient;
        }
    }

    private static HttpClient CreateDownloadClient()
    {
        var proxyServer = BedrockNetworkConfiguration.ProxyServer;
        if (!string.IsNullOrWhiteSpace(proxyServer) && !proxyServer.Contains("://", StringComparison.Ordinal))
            proxyServer = $"http://{proxyServer}";
        var hasProxyServer = Uri.TryCreate(proxyServer, UriKind.Absolute, out var proxyUri);
        var handler = new SocketsHttpHandler
        {
            UseProxy = !BedrockNetworkConfiguration.DisableSystemProxy ||
                       hasProxyServer,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            MaxConnectionsPerServer = DownloadConcurrency * 2,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        if (hasProxyServer) handler.Proxy = new WebProxy(proxyUri);
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BedrockNetworkConfiguration.UserAgent);
        return client;
    }

    private static async Task<bool> MatchesMd5Async(string path, string expectedMd5, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await MD5.HashDataAsync(stream, cancellationToken);
        return string.Equals(Convert.ToHexString(hash), expectedMd5, StringComparison.OrdinalIgnoreCase);
    }

    private static void CleanupPackageCache(string cacheDirectory)
    {
        lock (PackageCleanupLock)
        {
            try
            {
                foreach (var package in new DirectoryInfo(cacheDirectory).EnumerateFiles("*.insPack")
                             .Where(file => !ActivePackages.ContainsKey(file.FullName))
                             .OrderByDescending(file => file.LastWriteTimeUtc)
                             .Skip(RetainedPackageCount))
                    package.Delete();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning($"Failed to clean Bedrock package cache: {exception}");
            }
        }
    }

    private static void MarkPackageActive(string packagePath)
    {
        lock (PackageCleanupLock)
            ActivePackages[packagePath] = ActivePackages.GetValueOrDefault(packagePath) + 1;
    }

    private static void MarkPackageInactive(string packagePath)
    {
        lock (PackageCleanupLock)
        {
            if (ActivePackages.GetValueOrDefault(packagePath) <= 1)
                ActivePackages.Remove(packagePath);
            else
                ActivePackages[packagePath]--;
        }
    }

    private static void TouchPackage(string packagePath)
    {
        try
        {
            File.SetLastWriteTimeUtc(packagePath, DateTime.UtcNow);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Failed to update Bedrock package cache time: {exception}");
        }
    }
}

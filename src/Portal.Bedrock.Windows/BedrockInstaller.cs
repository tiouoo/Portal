using System;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using BedrockLauncher.Core;
using BedrockLauncher.Core.CoreOption;
using BedrockLauncher.Core.Utils;
using BedrockLauncher.Core.VersionJsons;
using Portal.Bedrock.Standard.Interface;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Portal.Bedrock;

public sealed class BedrockInstaller : IBedrockInstaller
{
    private const string VersionDatabaseUrl = "https://data.mcappx.com/v2/bedrock.json";
    private const int DownloadConcurrency = 8;
    private const int DownloadBufferSize = 1024 * 256;
    private const int SourceProbeBytes = 1024 * 1024;
    private static readonly SemaphoreSlim VersionLoadLock = new(1, 1);
    private static readonly HttpClient DownloadClient = CreateDownloadClient();
    private static IReadOnlyList<BedrockGdkVersion>? _cachedVersions;

    public async Task<IReadOnlyList<BedrockGdkVersion>> GetGdkVersionsAsync(bool refresh,
        CancellationToken cancellationToken)
    {
        if (!refresh && _cachedVersions is not null) return _cachedVersions;

        await VersionLoadLock.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && _cachedVersions is not null) return _cachedVersions;

            var database = await VersionsHelper.GetBuildDatabaseAsync(VersionDatabaseUrl, cancellationToken)
                           ?? throw new InvalidOperationException("未获取到基岩版版本数据。");
            var builds = new List<BedrockGdkVersion>();

            await foreach (var (_, build) in database.Builds.WithCancellation(cancellationToken))
            {
                if (build.Type is not (MinecraftGameTypeVersion.Release or MinecraftGameTypeVersion.Preview) ||
                    build.BuildType != MinecraftBuildTypeVersion.GDK ||
                    string.IsNullOrWhiteSpace(build.ID) ||
                    !build.Variations.Any(variation => variation.Arch == Architecture.X64 && variation.MetaData.Count > 0))
                    continue;

                builds.Add(new BedrockGdkVersion(build.ID, ParseReleaseTime(build.Date),
                    build.Type == MinecraftGameTypeVersion.Preview));
            }

            return _cachedVersions = builds.OrderByDescending(version => version.ReleaseTime)
                .ThenByDescending(version => ParseVersion(version.Id))
                .ThenByDescending(version => version.Id, StringComparer.Ordinal)
                .ToList();
        }
        finally
        {
            VersionLoadLock.Release();
        }
    }

    public async Task InstallGdkAsync(BedrockOnlineInstallRequest request, IProgress<BedrockInstallProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationPath);

        var destination = Path.GetFullPath(request.DestinationPath);
        if (Directory.Exists(destination))
            throw new InvalidOperationException("目标实例目录已存在。");

        var core = new BedrockCore();
        await core.InitAsync();
        await core.AutoCompleteGameInput();
        var build = await FindBuildAsync(request.Version, request.CancellationToken);
        var packageUrl = await core.GetPackageUri(build, Architecture.X64);
        var packagePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "xyz.tiouo.Portal", "Cache", "Bedrock", $"{request.Version.Id}.insPack");

        await DownloadPackageAsync(packageUrl, packagePath, build, progress, request.CancellationToken);
        request.CancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new BedrockInstallProgress(0, 0, string.Empty, "Extracting"));
        await core.InstallPackageAsync(new LocalGamePackageOptions
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
                progress?.Report(new BedrockInstallProgress(
                    extraction.CurrentCount,
                    extraction.TotalCount,
                    extraction.FileName,
                    InstallStates.Extracting.ToString())))
        });
    }

    private static Version ParseVersion(string version) => Version.TryParse(version, out var parsed) ? parsed : new Version();
    private static DateTime ParseReleaseTime(string date) => DateTime.TryParse(date, out var parsed) ? parsed : DateTime.MinValue;

    private static async Task<BuildInfo> FindBuildAsync(BedrockGdkVersion version, CancellationToken cancellationToken)
    {
        var database = await VersionsHelper.GetBuildDatabaseAsync(VersionDatabaseUrl, cancellationToken)
                       ?? throw new InvalidOperationException("未获取到基岩版版本数据。");
        var type = version.IsPreview ? MinecraftGameTypeVersion.Preview : MinecraftGameTypeVersion.Release;

        await foreach (var (_, build) in database.Builds.WithCancellation(cancellationToken))
        {
            if (build.ID == version.Id && build.Type == type && build.BuildType == MinecraftBuildTypeVersion.GDK &&
                build.Variations.Any(variation => variation.Arch == Architecture.X64 && variation.MetaData.Count > 0))
                return build;
        }

        throw new InvalidOperationException("所选基岩版版本已不可用，请刷新列表后重试。");
    }

    private static async Task DownloadPackageAsync(string url, string packagePath, BuildInfo build,
        IProgress<BedrockInstallProgress>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        var expectedMd5 = build.Variations.First(variation => variation.Arch == Architecture.X64).MD5;
        if (File.Exists(packagePath) && await MatchesMd5Async(packagePath, expectedMd5, cancellationToken))
        {
            progress?.Report(new BedrockInstallProgress(1, 1, Path.GetFileName(packagePath), "Using cached package"));
            return;
        }

        if (File.Exists(packagePath)) File.Delete(packagePath);
        progress?.Report(new BedrockInstallProgress(0, 0, string.Empty, "Selecting source"));
        var candidates = GetGdkDownloadUrls(url).ToList();
        var selected = await SelectFastestSourceAsync(candidates, cancellationToken);
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
                // Try the next Xbox CDN host, matching BedrockBoot's GDK fallback behavior.
                if (File.Exists(packagePath)) File.Delete(packagePath);
            }
        }

        throw new InvalidOperationException("无法下载或验证 GDK 安装包。");
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
        using var probeResponse = await DownloadClient.SendAsync(probeRequest, HttpCompletionOption.ResponseHeadersRead,
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
            using var response = await DownloadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (url, 0);
        }
        catch (HttpRequestException)
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
        using var response = await DownloadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.PartialContent)
            throw new HttpRequestException("下载源不支持分段下载。");
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
            using var response = await DownloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

    private static HttpClient CreateDownloadClient()
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            MaxConnectionsPerServer = DownloadConcurrency * 2,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Portal/1.0 Bedrock GDK Downloader");
        return client;
    }

    private static async Task<bool> MatchesMd5Async(string path, string expectedMd5, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedMd5)) return false;
        await using var stream = File.OpenRead(path);
        var hash = await MD5.HashDataAsync(stream, cancellationToken);
        return string.Equals(Convert.ToHexString(hash), expectedMd5, StringComparison.OrdinalIgnoreCase);
    }
}

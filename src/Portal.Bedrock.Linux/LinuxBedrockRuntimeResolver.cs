using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Portal.Bedrock.Standard.Interface;
using Portal.Localization;

namespace Portal.Bedrock.Linux;

public sealed record LinuxBedrockRuntime(string ProtonScript, string ProtonRoot, string PrefixPath,
    string SteamCompatPath);

public sealed record LinuxBedrockRuntimeProgress(string Message, long BytesReceived = 0, long TotalBytes = 0)
{
    public int Percentage => TotalBytes > 0 ? (int)Math.Min(100, BytesReceived * 100 / TotalBytes) : 0;
}

public sealed class LinuxBedrockRuntimeResolver
{
    public const string ProtonPathVariable = "PORTAL_PROTON_PATH";
    public const string PrefixPathVariable = "PORTAL_BEDROCK_PREFIX";

    private const int DownloadBufferSize = 1024 * 256;
    private const int SourceProbeBytes = 1024 * 1024;
    private const long MinimumSegmentSize = 8L * 1024 * 1024;
    private static readonly string[] ReleaseApiUrls =
    [
        "https://api.github.com/repos/Wyze3306/BedrockOnLinux/releases/tags/engine-wow64-archs-native17",
        "https://api.github.com/repos/Weather-OS/GDK-Proton/releases/latest",
        "https://api.github.com/repos/LukasPAH/GDK-Proton-Custom/releases/latest"
    ];

    private static readonly object DownloadClientLock = new();
    private static HttpClient? _downloadClient;
    private static int _downloadClientConfigurationVersion = -1;
    private static readonly SemaphoreSlim InstallLock = new(1, 1);

    public LinuxBedrockRuntime Resolve() => ResolveAsync().GetAwaiter().GetResult();

    public async Task<LinuxBedrockRuntime> ResolveAsync(Action<LinuxBedrockRuntimeProgress>? progress = null,
        CancellationToken cancellationToken = default, bool requireXUserRuntime = false)
    {
        EnsureSupportedPlatform();

        Trace.TraceInformation(LogLanguageManager.Instance.bedrockLaunch_resolvingProtonRuntime.CurrentValue());
        var protonScript = await ResolveProtonScriptAsync(progress, cancellationToken, requireXUserRuntime).ConfigureAwait(false);
        var protonRoot = Path.GetDirectoryName(protonScript)!;
        ApplyManagedRuntimePatch(protonRoot, progress);
        var prefixPath = ResolvePrefixPath();
        var steamClientPath = ResolveSteamCompatPath();

        Directory.CreateDirectory(prefixPath);
        Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockLaunch_protonRuntimeReady.CurrentValue(), protonScript, prefixPath));
        return new LinuxBedrockRuntime(protonScript, protonRoot, prefixPath, steamClientPath);
    }

    private static void ApplyManagedRuntimePatch(string protonRoot,
        Action<LinuxBedrockRuntimeProgress>? progress)
    {
        if (!IsManagedProtonRoot(protonRoot)) return;
        if (GdkRuntimePatcher.PatchCombaseRoOriginateErrorW(protonRoot))
            progress?.Invoke(new LinuxBedrockRuntimeProgress(CommonLanguageManager.Instance.bedrockLaunch_runtimePatchApplied.CurrentValue()));
    }

    private static bool IsManagedProtonRoot(string protonRoot)
    {
        var root = Path.GetFullPath(GetProtonInstallRoot());
        var candidate = Path.GetFullPath(protonRoot);
        return candidate == root ||
               candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    public static void EnsureSupportedPlatform()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException(
                CommonLanguageManager.Instance.bedrockLaunch_linuxPlatformOnlyX64Gdk.CurrentValue());
    }

    private static async Task<string> ResolveProtonScriptAsync(Action<LinuxBedrockRuntimeProgress>? progress,
        CancellationToken cancellationToken, bool requireXUserRuntime)
    {
        var configuredPath = Environment.GetEnvironmentVariable(ProtonPathVariable);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var configuredScript = NormalizeProtonScript(configuredPath);
            if (File.Exists(configuredScript))
            {
                Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockLaunch_usingConfiguredProton.CurrentValue(), configuredScript));
                return configuredScript;
            }

            throw new FileNotFoundException(
                string.Format(CommonLanguageManager.Instance.bedrockLaunch_protonPathInvalid.CurrentValue(), ProtonPathVariable),
                configuredScript);
        }

        var discovered = FindSteamProton(requireXUserRuntime);
        if (discovered is not null)
        {
            Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockLaunch_steamProtonDiscovered.CurrentValue(), discovered));
            return discovered;
        }

        var installed = FindInstalledProton(requireXUserRuntime);
        if (installed is not null)
        {
            Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockLaunch_usingInstalledProton.CurrentValue(), installed));
            return installed;
        }

        await InstallLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            installed = FindInstalledProton(requireXUserRuntime);
            if (installed is not null)
            {
                Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockLaunch_protonInstalledWhileWaiting.CurrentValue(), installed));
                return installed;
            }

            try
            {
                return await DownloadAndInstallAsync(progress, cancellationToken, requireXUserRuntime).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrockLaunch_autoDownloadProtonFailed.CurrentValue(), Environment.NewLine, exception));
                throw new InvalidOperationException(
                    string.Format(CommonLanguageManager.Instance.bedrockLaunch_autoDownloadProtonFailedManual.CurrentValue(), exception.Message, ProtonPathVariable), exception);
            }
        }
        finally
        {
            InstallLock.Release();
        }
    }

    private static string? FindSteamProton(bool requireXUserRuntime)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new[]
        {
            Path.Combine(home, ".steam", "root", "compatibilitytools.d"),
            Path.Combine(home, ".steam", "steam", "compatibilitytools.d"),
            Path.Combine(home, ".local", "share", "Steam", "compatibilitytools.d"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam",
                "compatibilitytools.d")
        };

        return roots.Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "proton", SearchOption.AllDirectories))
            .Where(IsGdkProton)
            .Where(path => !requireXUserRuntime || IsXUserRuntime(path))
            .OrderByDescending(IsXUserRuntime)
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string? FindInstalledProton(bool requireXUserRuntime)
    {
        var root = GetProtonInstallRoot();
        if (!Directory.Exists(root)) return null;

        var proton = Directory.EnumerateDirectories(root)
            .Where(directory => !Path.GetFileName(directory).StartsWith(".", StringComparison.Ordinal))
            .Select(FindProtonInDirectory)
            .Where(path => path is not null)
            .Select(path => path!)
            .Where(IsGdkProton)
            .Where(path => !requireXUserRuntime || IsXUserRuntime(path))
            .OrderByDescending(IsXUserRuntime)
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (proton is not null) EnsureExecutable(proton);
        return proton;
    }

    private static async Task<string> DownloadAndInstallAsync(Action<LinuxBedrockRuntimeProgress>? progress,
        CancellationToken cancellationToken, bool requireXUserRuntime)
    {
        progress?.Invoke(new LinuxBedrockRuntimeProgress(CommonLanguageManager.Instance.bedrockLaunch_queryingGdkProtonReleaseProgress.CurrentValue()));
        Trace.TraceInformation(LogLanguageManager.Instance.bedrockLaunch_queryingGdkProtonRelease.CurrentValue());
        var release = await GetReleaseAsync(cancellationToken, requireXUserRuntime).ConfigureAwait(false);
        var tag = SafePathSegment(release.TagName);
        var installRoot = GetProtonInstallRoot();
        var destination = Path.Combine(installRoot, $"{tag}-{SafePathSegment(release.Asset.Name)}");
        var existing = FindProtonInDirectory(destination);
        if (existing is not null)
        {
            Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockLaunch_gdkProtonAlreadyInstalled.CurrentValue(), existing));
            return existing;
        }

        var cacheDirectory = Path.Combine(GetCacheRoot(), "proton", tag);
        var archivePath = Path.Combine(cacheDirectory, SafePathSegment(release.Asset.Name));
        var expectedHash = ParseGitHubDigest(release.Asset.Digest);
        Directory.CreateDirectory(cacheDirectory);
        Directory.CreateDirectory(installRoot);

        if (!await IsCachedArchiveValidAsync(archivePath, expectedHash, cancellationToken).ConfigureAwait(false))
        {
            Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockLaunch_gdkProtonCacheInvalid.CurrentValue(), archivePath));
            await DownloadArchiveAsync(release.Asset.BrowserDownloadUrl, archivePath, expectedHash, progress,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockLaunch_usingValidatedProtonCache.CurrentValue(), archivePath));
            progress?.Invoke(new LinuxBedrockRuntimeProgress(string.Format(CommonLanguageManager.Instance.bedrockLaunch_usingValidatedProtonCacheProgress.CurrentValue(), release.Asset.Name)));
        }

        var staging = Path.Combine(installRoot, $".install-{tag}-{Guid.NewGuid():N}");
        try
        {
            progress?.Invoke(new LinuxBedrockRuntimeProgress(string.Format(CommonLanguageManager.Instance.bedrockLaunch_verifyingAndExtractingProgress.CurrentValue(), release.TagName)));
            Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockLaunch_verifyingAndExtractingProton.CurrentValue(), archivePath, staging));
            Directory.CreateDirectory(staging);
            await ValidateArchiveAsync(archivePath, staging, cancellationToken).ConfigureAwait(false);
            await ExtractArchiveAsync(archivePath, staging, progress, cancellationToken).ConfigureAwait(false);

            var proton = FindProtonInDirectory(staging)
                         ?? throw new InvalidDataException(CommonLanguageManager.Instance.bedrockLaunch_archiveMissingProtonScript.CurrentValue());
            var extractedRoot = Path.GetDirectoryName(proton)!;
            if (Directory.Exists(destination))
            {
                existing = FindProtonInDirectory(destination);
                if (existing is not null) return existing;
                throw new IOException(string.Format(CommonLanguageManager.Instance.bedrockLaunch_protonInstallIncomplete.CurrentValue(), destination));
            }

            try
            {
                Directory.Move(extractedRoot, destination);
            }
            catch (IOException) when (FindProtonInDirectory(destination) is not null)
            {
                proton = FindProtonInDirectory(destination)!;
                EnsureExecutable(proton);
                return proton;
            }
            proton = Path.Combine(destination, "proton");
            EnsureExecutable(proton);
            progress?.Invoke(new LinuxBedrockRuntimeProgress(string.Format(CommonLanguageManager.Instance.bedrockLaunch_gdkProtonInstalledProgress.CurrentValue(), release.TagName), 1, 1));
            Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockLaunch_gdkProtonInstalled.CurrentValue(), destination));
            return proton;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockLaunch_cleaningProtonStaging.CurrentValue(), staging));
                Directory.Delete(staging, true);
            }
        }
    }

    private static async Task<ProtonRelease> GetReleaseAsync(CancellationToken cancellationToken,
        bool xUserOnly = false)
    {
        var errors = new List<string>();
        var apiUrls = xUserOnly ? ReleaseApiUrls.Take(1) : ReleaseApiUrls.AsEnumerable();
        foreach (var apiUrl in apiUrls)
        {
            try
            {
                var release = await GetDownloadClient().GetFromJsonAsync<GitHubRelease>(apiUrl, cancellationToken)
                    .ConfigureAwait(false) ?? throw new InvalidDataException(CommonLanguageManager.Instance.bedrockLaunch_githubApiEmptyResponse.CurrentValue());
                var asset = release.Assets.FirstOrDefault(IsX64TarGzAsset)
                            ?? throw new InvalidDataException(CommonLanguageManager.Instance.bedrockLaunch_releaseNoX64Asset.CurrentValue());
                if (string.IsNullOrWhiteSpace(release.TagName) || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
                    throw new InvalidDataException(CommonLanguageManager.Instance.bedrockLaunch_releaseMissingTagOrUrl.CurrentValue());
                return new ProtonRelease(release.TagName, asset);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrockLaunch_queryProtonReleaseFailed.CurrentValue(), apiUrl, Environment.NewLine, exception));
                errors.Add($"{new Uri(apiUrl).Host}: {exception.Message}");
            }
        }

        throw new HttpRequestException(string.Join(CommonLanguageManager.Instance.bedrockLaunch_errorListSeparator.CurrentValue(), errors));
    }

    private static bool IsX64TarGzAsset(GitHubAsset asset)
    {
        var name = asset.Name;
        if (!name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) return false;
        return !name.Contains("arm", StringComparison.OrdinalIgnoreCase) &&
               !name.Contains("aarch", StringComparison.OrdinalIgnoreCase) &&
               !name.Contains("source", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task DownloadArchiveAsync(string url, string archivePath, string? expectedHash,
        Action<LinuxBedrockRuntimeProgress>? progress, CancellationToken cancellationToken)
    {
        var temporaryPath = archivePath + $".{Guid.NewGuid():N}.download";
        try
        {
            progress?.Invoke(new LinuxBedrockRuntimeProgress(CommonLanguageManager.Instance.bedrockLaunch_rankingDownloadSources.CurrentValue()));
            var sources = await RankDownloadSourcesAsync(url, cancellationToken).ConfigureAwait(false);
            var errors = new List<string>();
            foreach (var source in sources)
            {
                for (var attempt = 1; attempt <= BedrockNetworkConfiguration.MaxRetryCount; attempt++)
                {
                    try
                    {
                        if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                        Trace.TraceInformation(
                            string.Format(LogLanguageManager.Instance.bedrockLaunch_downloadingProtonSource.CurrentValue(), source.Url, source.SupportsRange, attempt));
                        if (BedrockNetworkConfiguration.EnableFragmentDownload && source.SupportsRange && source.Total > 0)
                        {
                            try
                            {
                                await DownloadMultiPartAsync(source.Url, temporaryPath, source.Total, progress,
                                    cancellationToken).ConfigureAwait(false);
                            }
                            catch (HttpRequestException exception)
                            {
                                Trace.TraceWarning(string.Format(LogLanguageManager.Instance.bedrockLaunch_protonFragmentFallback.CurrentValue(), exception.Message));
                                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                                await DownloadSinglePartAsync(source.Url, temporaryPath, source.Total, progress,
                                    cancellationToken).ConfigureAwait(false);
                            }
                        }
                        else
                            await DownloadSinglePartAsync(source.Url, temporaryPath, source.Total, progress,
                                cancellationToken).ConfigureAwait(false);

                        await using var downloaded = File.OpenRead(temporaryPath);
                        var actualHash = Convert.ToHexString(
                            await SHA256.HashDataAsync(downloaded, cancellationToken).ConfigureAwait(false));
                        if (expectedHash is not null &&
                            !string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException(CommonLanguageManager.Instance.bedrockLaunch_sha256Mismatch.CurrentValue());

                        File.Move(temporaryPath, archivePath, true);
                        await File.WriteAllTextAsync(GetHashSidecarPath(archivePath), actualHash.ToLowerInvariant() + "\n",
                            cancellationToken).ConfigureAwait(false);
                        progress?.Invoke(new LinuxBedrockRuntimeProgress(CommonLanguageManager.Instance.bedrockLaunch_protonDownloadVerified.CurrentValue(),
                            source.Total, source.Total));
                        return;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        errors.Add(string.Format(CommonLanguageManager.Instance.bedrockLaunch_attemptError.CurrentValue(), new Uri(source.Url).Host, attempt, exception.Message));
                        Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrockLaunch_protonDownloadFailed.CurrentValue(), Environment.NewLine, exception));
                        if (attempt < BedrockNetworkConfiguration.MaxRetryCount)
                            await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            throw new HttpRequestException(string.Format(CommonLanguageManager.Instance.bedrockLaunch_allSourcesFailed.CurrentValue(), string.Join(CommonLanguageManager.Instance.bedrockLaunch_errorListSeparator.CurrentValue(), errors)));
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockLaunch_cleaningProtonTempDownload.CurrentValue(), temporaryPath));
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<IReadOnlyList<DownloadSource>> RankDownloadSourcesAsync(string officialUrl,
        CancellationToken cancellationToken)
    {
        var urls = BuildDownloadUrls(officialUrl);
        var probes = await Task.WhenAll(urls.Select(url => ProbeSourceAsync(url, cancellationToken)))
            .ConfigureAwait(false);
        var available = probes.Where(source => source is not null).Select(source => source!).ToList();
        if (available.Count == 0)
            throw new HttpRequestException(CommonLanguageManager.Instance.bedrockLaunch_noSourcesAccessible.CurrentValue());
        return available.OrderByDescending(source => source.SupportsRange)
            .ThenByDescending(source => source.Speed).ToArray();
    }

    private static async Task<DownloadSource?> ProbeSourceAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, SourceProbeBytes - 1);
            var stopwatch = Stopwatch.StartNew();
            using var response = await GetDownloadClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var supportsRange = response.StatusCode == HttpStatusCode.PartialContent &&
                                response.Content.Headers.ContentRange?.From == 0;
            var total = response.Content.Headers.ContentRange?.Length ?? response.Content.Headers.ContentLength ?? 0;
            if (!supportsRange) return new DownloadSource(url, 0, total, false);

            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var buffer = new byte[DownloadBufferSize];
            var received = 0;
            while (received < SourceProbeBytes)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0,
                    Math.Min(buffer.Length, SourceProbeBytes - received)), timeout.Token).ConfigureAwait(false);
                if (read == 0) break;
                received += read;
            }
            return new DownloadSource(url,
                stopwatch.Elapsed.TotalSeconds > 0 ? received / stopwatch.Elapsed.TotalSeconds : 0, total, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Trace.TraceWarning(string.Format(LogLanguageManager.Instance.bedrockLaunch_protonSourceProbeFailed.CurrentValue(), url, exception.Message));
            return null;
        }
    }

    private static async Task DownloadMultiPartAsync(string url, string path, long total,
        Action<LinuxBedrockRuntimeProgress>? progress, CancellationToken cancellationToken)
    {
        var segmentCount = (int)Math.Min(BedrockNetworkConfiguration.MaxFragmentCount,
            Math.Max(1, (total + MinimumSegmentSize - 1) / MinimumSegmentSize));
        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Write,
                         DownloadBufferSize, FileOptions.Asynchronous))
            file.SetLength(total);

        long downloaded = 0;
        var reporter = CreateProgressReporter(progress, total, () => Interlocked.Read(ref downloaded));
        var segmentSize = (total + segmentCount - 1) / segmentCount;
        await Task.WhenAll(Enumerable.Range(0, segmentCount).Select(async index =>
        {
            var start = index * segmentSize;
            if (start >= total) return;
            var end = Math.Min(total, start + segmentSize) - 1;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(start, end);
            using var response = await GetDownloadClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            var range = response.Content.Headers.ContentRange;
            if (response.StatusCode != HttpStatusCode.PartialContent || range?.From != start || range.To != end)
                throw new HttpRequestException(string.Format(CommonLanguageManager.Instance.bedrockLaunch_invalidFragmentRange.CurrentValue(), start, end));
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Write,
                DownloadBufferSize, FileOptions.Asynchronous | FileOptions.RandomAccess);
            output.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[DownloadBufferSize];
            long segmentReceived = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                segmentReceived += read;
                if (segmentReceived > end - start + 1)
                    throw new HttpRequestException(string.Format(CommonLanguageManager.Instance.bedrockLaunch_fragmentExceedsLength.CurrentValue(), start, end));
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                Interlocked.Add(ref downloaded, read);
                reporter();
            }
            if (segmentReceived != end - start + 1)
                throw new EndOfStreamException(string.Format(CommonLanguageManager.Instance.bedrockLaunch_fragmentIncomplete.CurrentValue(), start, end));
        })).ConfigureAwait(false);
        progress?.Invoke(new LinuxBedrockRuntimeProgress(CommonLanguageManager.Instance.bedrockLaunch_downloadingGdkProton.CurrentValue(), total, total));
    }

    private static async Task DownloadSinglePartAsync(string url, string path, long expectedTotal,
        Action<LinuxBedrockRuntimeProgress>? progress, CancellationToken cancellationToken)
    {
        using var response = await GetDownloadClient().GetAsync(url, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? expectedTotal;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            DownloadBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        long downloaded = 0;
        var reporter = CreateProgressReporter(progress, total, () => Interlocked.Read(ref downloaded));
        var buffer = new byte[DownloadBufferSize];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref downloaded, read);
            reporter();
        }
        if (total > 0 && downloaded != total) throw new EndOfStreamException(CommonLanguageManager.Instance.bedrockLaunch_downloadIncomplete.CurrentValue());
    }

    private static Action CreateProgressReporter(Action<LinuxBedrockRuntimeProgress>? progress, long total,
        Func<long> getDownloaded)
    {
        var gate = new object();
        var lastReport = Stopwatch.GetTimestamp();
        var lastPercentage = -1;
        return () =>
        {
            if (progress is null) return;
            lock (gate)
            {
                var downloaded = getDownloaded();
                var percentage = total > 0 ? (int)(downloaded * 100 / total) : 0;
                if (percentage == lastPercentage && Stopwatch.GetElapsedTime(lastReport) < TimeSpan.FromSeconds(1)) return;
                lastPercentage = percentage;
                lastReport = Stopwatch.GetTimestamp();
                progress(new LinuxBedrockRuntimeProgress(CommonLanguageManager.Instance.bedrockLaunch_downloadingGdkProton.CurrentValue(), downloaded, total));
            }
        };
    }

    private static IReadOnlyList<string> BuildDownloadUrls(string officialUrl)
    {
        var urls = new List<string>();
        var mirror = ApplyGithubMirror(officialUrl);
        if (!string.Equals(mirror, officialUrl, StringComparison.Ordinal)) urls.Add(mirror);
        urls.Add(officialUrl);
        return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static string ApplyGithubMirror(string url)
    {
        if (!BedrockNetworkConfiguration.EnableGithubMirror ||
            string.IsNullOrWhiteSpace(BedrockNetworkConfiguration.GithubMirrorUrl)) return url;
        var mirror = BedrockNetworkConfiguration.GithubMirrorUrl.Trim();
        if (mirror.Contains("{url}", StringComparison.OrdinalIgnoreCase))
        {
            var index = mirror.IndexOf("{url}", StringComparison.OrdinalIgnoreCase);
            return mirror[..index] + url + mirror[(index + 5)..];
        }
        if (!mirror.Contains("://", StringComparison.Ordinal)) mirror = "https://" + mirror;
        if (!Uri.TryCreate(mirror, UriKind.Absolute, out var mirrorUri)) return url;
        if (!BedrockNetworkConfiguration.GithubMirrorDirect) return $"{mirror.TrimEnd('/')}/{url}";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var original)) return url;
        var builder = new UriBuilder(original)
        {
            Scheme = mirrorUri.Scheme,
            Host = mirrorUri.Host,
            Port = mirrorUri.IsDefaultPort ? -1 : mirrorUri.Port,
            Path = mirrorUri.AbsolutePath.TrimEnd('/') + original.AbsolutePath
        };
        return builder.Uri.AbsoluteUri;
    }

    private static async Task<bool> IsCachedArchiveValidAsync(string archivePath, string? expectedHash,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(archivePath)) return false;
        var hash = expectedHash;
        if (hash is null)
        {
            var sidecar = GetHashSidecarPath(archivePath);
            if (!File.Exists(sidecar)) return false;
            hash = (await File.ReadAllTextAsync(sidecar, cancellationToken).ConfigureAwait(false)).Trim();
            if (hash.Length != 64) return false;
        }

        await using var stream = File.OpenRead(archivePath);
        var actual = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(Convert.ToHexString(actual), hash, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ValidateArchiveAsync(string archivePath, string extractionRoot,
        CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(archivePath);
        Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockLaunch_validatingProtonArchive.CurrentValue(), archivePath));
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        TarEntry? entry;
        while ((entry = await reader.GetNextEntryAsync(false, cancellationToken).ConfigureAwait(false)) is not null)
        {
            var type = (char)entry.EntryType;
            if (type is 'g' or 'x') continue;
            ValidateArchivePath(extractionRoot, entry.Name, extractionRoot);
            if (type is not ('\0' or '0' or '1' or '2' or '5' or '7'))
                throw new InvalidDataException(string.Format(CommonLanguageManager.Instance.bedrockLaunch_archiveUnsupportedEntryType.CurrentValue(), entry.EntryType));
            if (type == '1') ValidateArchivePath(extractionRoot, entry.LinkName, extractionRoot);
            if (type == '2')
            {
                var entryDirectory = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(extractionRoot, entry.Name)))!;
                ValidateArchivePath(extractionRoot, entry.LinkName, entryDirectory);
            }
        }
    }

    private static async Task ExtractArchiveAsync(string archivePath, string extractionRoot,
        Action<LinuxBedrockRuntimeProgress>? progress, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(archivePath);
        Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockLaunch_extractingProtonArchive.CurrentValue(), archivePath, extractionRoot));
        var total = file.Length;
        await using var counting = new CountingStream(file);
        await using var gzip = new GZipStream(counting, CompressionMode.Decompress);
        var extraction = TarFile.ExtractToDirectoryAsync(gzip, extractionRoot, false, cancellationToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        try
        {
            while (!extraction.IsCompleted &&
                   await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                progress?.Invoke(new LinuxBedrockRuntimeProgress(CommonLanguageManager.Instance.bedrockLaunch_extractingGdkProton.CurrentValue(), counting.BytesRead, total));
            }

            await extraction.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { await extraction.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            throw;
        }

        progress?.Invoke(new LinuxBedrockRuntimeProgress(CommonLanguageManager.Instance.bedrockLaunch_extractingGdkProton.CurrentValue(), total, total));
    }

    private static void ValidateArchivePath(string extractionRoot, string? archivePath, string relativeRoot)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || Path.IsPathRooted(archivePath))
            throw new InvalidDataException(CommonLanguageManager.Instance.bedrockLaunch_archiveInvalidPath.CurrentValue());
        var root = Path.GetFullPath(extractionRoot);
        var candidate = Path.GetFullPath(Path.Combine(relativeRoot, archivePath));
        if (candidate != root && !candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException(string.Format(CommonLanguageManager.Instance.bedrockLaunch_archivePathOutOfRoot.CurrentValue(), archivePath));
    }

    private static string ResolvePrefixPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable(PrefixPathVariable);
        if (!string.IsNullOrWhiteSpace(configuredPath)) return Path.GetFullPath(configuredPath);
        return Path.Combine(GetDataHome(), "Portal", "Bedrock", "proton-prefix");
    }

    private static string ResolveSteamCompatPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(home, ".steam", "root"),
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".local", "share", "Steam"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam")
        };

        var installed = candidates.FirstOrDefault(Directory.Exists);
        if (installed is not null) return installed;

        
        
        
        var managed = Path.Combine(GetDataHome(), "Portal", "Bedrock", "steam-client");
        Directory.CreateDirectory(managed);
        return managed;
    }

    private static string NormalizeProtonScript(string path)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        return Directory.Exists(fullPath) ? Path.Combine(fullPath, "proton") : fullPath;
    }

    private static string? FindProtonInDirectory(string directory) => Directory.Exists(directory)
        ? Directory.EnumerateFiles(directory, "proton", SearchOption.AllDirectories).FirstOrDefault()
        : null;

    private static void EnsureExecutable(string proton)
    {
        var mode = File.GetUnixFileMode(proton);
        File.SetUnixFileMode(proton, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute |
            UnixFileMode.OtherExecute);
    }

    private static string GetProtonInstallRoot() => Path.Combine(GetDataHome(), "Portal", "Bedrock", "proton");

    private static string GetDataHome()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return string.IsNullOrWhiteSpace(dataHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
            : Path.GetFullPath(dataHome);
    }

    private static string GetCacheRoot()
    {
        var cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        return string.IsNullOrWhiteSpace(cacheHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "Portal", "Bedrock")
            : Path.Combine(Path.GetFullPath(cacheHome), "Portal", "Bedrock");
    }

    private static string SafePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().Append(Path.DirectorySeparatorChar)
            .Append(Path.AltDirectorySeparatorChar).ToHashSet();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        if (string.IsNullOrWhiteSpace(safe) || safe is "." or "..")
            throw new InvalidDataException(CommonLanguageManager.Instance.bedrockLaunch_releaseNameUnsafe.CurrentValue());
        return safe;
    }

    private static string? ParseGitHubDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        const string prefix = "sha256:";
        if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || digest.Length != prefix.Length + 64)
            throw new InvalidDataException(CommonLanguageManager.Instance.bedrockLaunch_unsupportedDigestFormat.CurrentValue());
        return digest[prefix.Length..];
    }

    private static string GetHashSidecarPath(string archivePath) => archivePath + ".sha256";

    private static bool IsGdkProton(string path) =>
        path.Contains("gdk-proton", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("protongdk", StringComparison.OrdinalIgnoreCase);

    private static bool IsXUserRuntime(string path) =>
        path.Contains("xuser", StringComparison.OrdinalIgnoreCase);

    private static HttpClient GetDownloadClient()
    {
        lock (DownloadClientLock)
        {
            if (_downloadClient is not null &&
                _downloadClientConfigurationVersion == BedrockNetworkConfiguration.Version) return _downloadClient;
            _downloadClient?.Dispose();
            var proxyServer = BedrockNetworkConfiguration.ProxyServer;
            var hasProxy = Uri.TryCreate(proxyServer, UriKind.Absolute, out var proxyUri);
            var handler = new SocketsHttpHandler
            {
                UseProxy = !BedrockNetworkConfiguration.DisableSystemProxy || hasProxy,
                Proxy = hasProxy ? new WebProxy(proxyUri!) : null,
                AutomaticDecompression = DecompressionMethods.All,
                MaxConnectionsPerServer = Math.Max(8, BedrockNetworkConfiguration.MaxFragmentCount * 2),
                ConnectTimeout = TimeSpan.FromSeconds(20)
            };
            _downloadClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            _downloadClient.DefaultRequestHeaders.UserAgent.ParseAdd(BedrockNetworkConfiguration.UserAgent);
            _downloadClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            _downloadClientConfigurationVersion = BedrockNetworkConfiguration.Version;
            return _downloadClient;
        }
    }

    private sealed record ProtonRelease(string TagName, GitHubAsset Asset);
    private sealed record DownloadSource(string Url, double Speed, long Total, bool SupportsRange);

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("assets")]
        public GitHubAsset[] Assets { get; init; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = string.Empty;

        [JsonPropertyName("digest")]
        public string? Digest { get; init; }
    }

    private sealed class CountingStream(Stream inner) : Stream
    {
        private long _bytesRead;

        public long BytesRead => _bytesRead;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            _bytesRead += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = inner.Read(buffer);
            _bytesRead += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            _bytesRead += read;
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            _bytesRead += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

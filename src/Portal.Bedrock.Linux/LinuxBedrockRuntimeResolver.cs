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
        "https://api.github.com/repos/Weather-OS/GDK-Proton/releases/latest",
        "https://api.github.com/repos/LukasPAH/GDK-Proton-Custom/releases/latest"
    ];

    private static readonly object DownloadClientLock = new();
    private static HttpClient? _downloadClient;
    private static int _downloadClientConfigurationVersion = -1;
    private static readonly SemaphoreSlim InstallLock = new(1, 1);

    public LinuxBedrockRuntime Resolve() => ResolveAsync().GetAwaiter().GetResult();

    public async Task<LinuxBedrockRuntime> ResolveAsync(Action<LinuxBedrockRuntimeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureSupportedPlatform();

        Trace.TraceInformation("开始解析 Linux 基岩版 Proton 运行时。");
        var protonScript = await ResolveProtonScriptAsync(progress, cancellationToken).ConfigureAwait(false);
        var protonRoot = Path.GetDirectoryName(protonScript)!;
        ApplyManagedRuntimePatch(protonRoot, progress);
        var prefixPath = ResolvePrefixPath();
        var steamClientPath = ResolveSteamCompatPath();

        Directory.CreateDirectory(prefixPath);
        Trace.TraceInformation($"Linux 基岩版 Proton 运行时已就绪：{protonScript}，前缀：{prefixPath}。");
        return new LinuxBedrockRuntime(protonScript, protonRoot, prefixPath, steamClientPath);
    }

    private static void ApplyManagedRuntimePatch(string protonRoot,
        Action<LinuxBedrockRuntimeProgress>? progress)
    {
        if (!IsManagedProtonRoot(protonRoot)) return;
        if (GdkRuntimePatcher.PatchCombaseRoOriginateErrorW(protonRoot))
            progress?.Invoke(new LinuxBedrockRuntimeProgress("已应用运行时兼容补丁（combase.RoOriginateErrorW）"));
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
                "Portal Bedrock Linux 仅支持 Linux x64，且只能启动 GDK 构建。");
    }

    private static async Task<string> ResolveProtonScriptAsync(Action<LinuxBedrockRuntimeProgress>? progress,
        CancellationToken cancellationToken)
    {
        var configuredPath = Environment.GetEnvironmentVariable(ProtonPathVariable);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var configuredScript = NormalizeProtonScript(configuredPath);
            if (File.Exists(configuredScript))
            {
                Trace.TraceInformation($"使用 PORTAL_PROTON_PATH 指定的 Proton：{configuredScript}。");
                return configuredScript;
            }

            throw new FileNotFoundException(
                $"{ProtonPathVariable} 未指向可用的 Proton 脚本。请将它设置为 proton 文件或包含 proton 文件的目录。",
                configuredScript);
        }

        var discovered = FindSteamProton();
        if (discovered is not null)
        {
            Trace.TraceInformation($"发现 Steam 安装的 Proton：{discovered}。");
            return discovered;
        }

        var installed = FindInstalledProton();
        if (installed is not null)
        {
            Trace.TraceInformation($"使用 Portal 已安装的 Proton：{installed}。");
            return installed;
        }

        await InstallLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            installed = FindInstalledProton();
            if (installed is not null)
            {
                Trace.TraceInformation($"等待安装锁期间 Proton 已安装：{installed}。");
                return installed;
            }

            try
            {
                return await DownloadAndInstallAsync(progress, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Trace.TraceError($"自动下载 Linux 基岩版 GDK-Proton 失败。{Environment.NewLine}{exception}");
                throw new InvalidOperationException(
                    $"自动下载 GDK-Proton 失败：{exception.Message}。可手动下载兼容的 Linux x64 GDK-Proton，" +
                    $"并将 {ProtonPathVariable} 设置为其 proton 脚本或安装目录。", exception);
            }
        }
        finally
        {
            InstallLock.Release();
        }
    }

    private static string? FindSteamProton()
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
            .Where(path => IsGdkProton(path) && !IsKnownBrokenXUserRuntime(path))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string? FindInstalledProton()
    {
        var root = GetProtonInstallRoot();
        if (!Directory.Exists(root)) return null;

        var proton = Directory.EnumerateDirectories(root)
            .Where(directory => !Path.GetFileName(directory).StartsWith(".", StringComparison.Ordinal))
            .Select(FindProtonInDirectory)
            .Where(path => path is not null)
            .Select(path => path!)
            .Where(path => IsGdkProton(path) && !IsKnownBrokenXUserRuntime(path))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (proton is not null) EnsureExecutable(proton);
        return proton;
    }

    private static async Task<string> DownloadAndInstallAsync(Action<LinuxBedrockRuntimeProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Invoke(new LinuxBedrockRuntimeProgress("正在查询 GDK-Proton x64 release"));
        Trace.TraceInformation("查询 Linux 基岩版 GDK-Proton release。");
        var release = await GetReleaseAsync(cancellationToken).ConfigureAwait(false);
        var tag = SafePathSegment(release.TagName);
        var installRoot = GetProtonInstallRoot();
        var destination = Path.Combine(installRoot, $"{tag}-{SafePathSegment(release.Asset.Name)}");
        var existing = FindProtonInDirectory(destination);
        if (existing is not null)
        {
            Trace.TraceInformation($"GDK-Proton 已安装：{existing}。");
            return existing;
        }

        var cacheDirectory = Path.Combine(GetCacheRoot(), "proton", tag);
        var archivePath = Path.Combine(cacheDirectory, SafePathSegment(release.Asset.Name));
        var expectedHash = ParseGitHubDigest(release.Asset.Digest);
        Directory.CreateDirectory(cacheDirectory);
        Directory.CreateDirectory(installRoot);

        if (!await IsCachedArchiveValidAsync(archivePath, expectedHash, cancellationToken).ConfigureAwait(false))
        {
            Trace.TraceInformation($"GDK-Proton 缓存不存在或校验失败，开始下载：{archivePath}。");
            await DownloadArchiveAsync(release.Asset.BrowserDownloadUrl, archivePath, expectedHash, progress,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Trace.TraceInformation($"使用已校验的 GDK-Proton 缓存：{archivePath}。");
            progress?.Invoke(new LinuxBedrockRuntimeProgress($"使用已校验的 Proton 缓存：{release.Asset.Name}"));
        }

        var staging = Path.Combine(installRoot, $".install-{tag}-{Guid.NewGuid():N}");
        try
        {
            progress?.Invoke(new LinuxBedrockRuntimeProgress($"正在验证并解压 GDK-Proton {release.TagName}"));
            Trace.TraceInformation($"验证并解压 GDK-Proton：{archivePath} -> {staging}。");
            Directory.CreateDirectory(staging);
            await ValidateArchiveAsync(archivePath, staging, cancellationToken).ConfigureAwait(false);
            await ExtractArchiveAsync(archivePath, staging, progress, cancellationToken).ConfigureAwait(false);

            var proton = FindProtonInDirectory(staging)
                         ?? throw new InvalidDataException("GDK-Proton 归档中缺少 proton 启动脚本");
            var extractedRoot = Path.GetDirectoryName(proton)!;
            if (Directory.Exists(destination))
            {
                existing = FindProtonInDirectory(destination);
                if (existing is not null) return existing;
                throw new IOException($"Proton 安装目录已存在但不完整：{destination}");
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
            progress?.Invoke(new LinuxBedrockRuntimeProgress($"GDK-Proton {release.TagName} 已安装", 1, 1));
            Trace.TraceInformation($"GDK-Proton 安装完成：{destination}。");
            return proton;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Trace.TraceInformation($"清理 GDK-Proton 临时解压目录：{staging}。");
                Directory.Delete(staging, true);
            }
        }
    }

    private static async Task<ProtonRelease> GetReleaseAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var apiUrl in ReleaseApiUrls)
        {
            try
            {
                var release = await GetDownloadClient().GetFromJsonAsync<GitHubRelease>(apiUrl, cancellationToken)
                    .ConfigureAwait(false) ?? throw new InvalidDataException("GitHub API 返回空响应");
                var asset = release.Assets.FirstOrDefault(IsX64TarGzAsset)
                            ?? throw new InvalidDataException("release 中没有 Linux x64 GDK-Proton tar.gz 资产");
                if (string.IsNullOrWhiteSpace(release.TagName) || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
                    throw new InvalidDataException("release 元数据缺少 tag 或下载 URL");
                return new ProtonRelease(release.TagName, asset);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Trace.TraceError($"查询 GDK-Proton release 失败：{apiUrl}{Environment.NewLine}{exception}");
                errors.Add($"{new Uri(apiUrl).Host}: {exception.Message}");
            }
        }

        throw new HttpRequestException(string.Join("；", errors));
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
            progress?.Invoke(new LinuxBedrockRuntimeProgress("正在测速 GitHub 镜像和官方源"));
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
                            $"下载 GDK-Proton：source={source.Url}，range={source.SupportsRange}，attempt={attempt}。");
                        if (BedrockNetworkConfiguration.EnableFragmentDownload && source.SupportsRange && source.Total > 0)
                        {
                            try
                            {
                                await DownloadMultiPartAsync(source.Url, temporaryPath, source.Total, progress,
                                    cancellationToken).ConfigureAwait(false);
                            }
                            catch (HttpRequestException exception)
                            {
                                Trace.TraceWarning($"GDK-Proton 分片下载不可用，回退单流：{exception.Message}");
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
                            throw new InvalidDataException("GDK-Proton 归档 SHA256 与 GitHub digest 不一致");

                        File.Move(temporaryPath, archivePath, true);
                        await File.WriteAllTextAsync(GetHashSidecarPath(archivePath), actualHash.ToLowerInvariant() + "\n",
                            cancellationToken).ConfigureAwait(false);
                        progress?.Invoke(new LinuxBedrockRuntimeProgress("GDK-Proton 下载及 SHA256 校验完成",
                            source.Total, source.Total));
                        return;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        errors.Add($"{new Uri(source.Url).Host} 第 {attempt} 次：{exception.Message}");
                        Trace.TraceError($"GDK-Proton 下载失败。{Environment.NewLine}{exception}");
                        if (attempt < BedrockNetworkConfiguration.MaxRetryCount)
                            await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            throw new HttpRequestException($"所有 GDK-Proton 下载源均失败：{string.Join("；", errors)}");
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                Trace.TraceInformation($"清理 GDK-Proton 临时下载文件：{temporaryPath}。");
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
            throw new HttpRequestException("GitHub 镜像和官方源均无法访问。");
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
            Trace.TraceWarning($"GDK-Proton 下载源探测失败：{url}，{exception.Message}");
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
                throw new HttpRequestException($"下载源返回了无效分片：{start}-{end}。");
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
                    throw new HttpRequestException($"下载分片超过预期长度：{start}-{end}。");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                Interlocked.Add(ref downloaded, read);
                reporter();
            }
            if (segmentReceived != end - start + 1)
                throw new EndOfStreamException($"下载分片不完整：{start}-{end}。");
        })).ConfigureAwait(false);
        progress?.Invoke(new LinuxBedrockRuntimeProgress("正在下载 GDK-Proton", total, total));
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
        if (total > 0 && downloaded != total) throw new EndOfStreamException("GDK-Proton 下载不完整。");
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
                progress(new LinuxBedrockRuntimeProgress("正在下载 GDK-Proton", downloaded, total));
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

    private static string ApplyGithubMirror(string url)
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
        Trace.TraceInformation($"校验 GDK-Proton 归档路径：{archivePath}。");
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        TarEntry? entry;
        while ((entry = await reader.GetNextEntryAsync(false, cancellationToken).ConfigureAwait(false)) is not null)
        {
            var type = (char)entry.EntryType;
            if (type is 'g' or 'x') continue;
            ValidateArchivePath(extractionRoot, entry.Name, extractionRoot);
            if (type is not ('\0' or '0' or '1' or '2' or '5' or '7'))
                throw new InvalidDataException($"归档包含不允许的 tar 项类型：{entry.EntryType}");
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
        Trace.TraceInformation($"解压 GDK-Proton 归档：{archivePath} -> {extractionRoot}。");
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
                progress?.Invoke(new LinuxBedrockRuntimeProgress("正在解压 GDK-Proton", counting.BytesRead, total));
            }

            await extraction.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { await extraction.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            throw;
        }

        progress?.Invoke(new LinuxBedrockRuntimeProgress("正在解压 GDK-Proton", total, total));
    }

    private static void ValidateArchivePath(string extractionRoot, string? archivePath, string relativeRoot)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || Path.IsPathRooted(archivePath))
            throw new InvalidDataException("归档包含空路径或绝对路径");
        var root = Path.GetFullPath(extractionRoot);
        var candidate = Path.GetFullPath(Path.Combine(relativeRoot, archivePath));
        if (candidate != root && !candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException($"归档路径越出安装目录：{archivePath}");
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
            throw new InvalidDataException("release 名称不能安全映射到本地路径");
        return safe;
    }

    private static string? ParseGitHubDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        const string prefix = "sha256:";
        if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || digest.Length != prefix.Length + 64)
            throw new InvalidDataException("GitHub release 提供了不支持的 digest 格式");
        return digest[prefix.Length..];
    }

    private static string GetHashSidecarPath(string archivePath) => archivePath + ".sha256";

    private static bool IsGdkProton(string path) =>
        path.Contains("gdk-proton", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("protongdk", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownBrokenXUserRuntime(string path) =>
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

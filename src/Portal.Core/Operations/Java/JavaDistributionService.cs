using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using MinecraftLaunch;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Utilities;
using Portal.Core.Minecraft.Instance.Java;
using SharpCompress.Compressors.Xz;

namespace Portal.Core.Operations.Java;

public sealed record JavaDistribution(string Vendor, string Product, IReadOnlyList<JavaDistributionVersion> Versions)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Product) ? Vendor : $"{Vendor} · {Product}";
}

public sealed record JavaDistributionVersion(
    int MajorVersion,
    string FullVersion,
    string Vendor,
    string Product,
    string Url,
    string Sha256,
    long Size,
    string ArchiveName);

public sealed record JavaInstallProgress(
    string Stage,
    double? Fraction,
    long DownloadedBytes,
    long TotalBytes,
    double SpeedBytesPerSecond);

public delegate void JavaInstallProgressHandler(JavaInstallProgress progress);

public static class JavaDistributionService
{
    private const string FeedUrl = "https://download.jetbrains.com/jdk/feed/v1/jdks.json.xz";

    private const string MojangRuntimeIndexUrl =
        "https://piston-meta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

    private static readonly SemaphoreSlim FeedLock = new(1, 1);
    private static IReadOnlyList<JavaDistributionVersion>? FeedCache;
    private static HttpClient Client => HttpUtil.Client;

    public static async Task<IReadOnlyList<JavaDistribution>> GetDistributionsAsync(
        CancellationToken cancellationToken = default)
    {
        var entries = await GetFeedAsync(cancellationToken);
        return entries.GroupBy(x => x.Vendor, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x =>
                new JavaDistribution(x.Key, x.First().Product, x.OrderByDescending(v => v.MajorVersion).ToList()))
            .ToList();
    }

    public static async Task<IReadOnlyList<JavaDistributionVersion>> GetVersionsAsync(string vendor,
        CancellationToken cancellationToken = default)
    {
        return (await GetFeedAsync(cancellationToken))
            .Where(x => string.Equals(x.Vendor, vendor, StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.MajorVersion).Select(x => x.First()).OrderByDescending(x => x.MajorVersion).ToList();
    }

    public static async Task<JavaDistributionVersion?> GetFastestVersionAsync(int majorVersion,
        CancellationToken cancellationToken = default)
    {
        var candidates = (await GetFeedAsync(cancellationToken)).Where(x => x.MajorVersion == majorVersion).ToList();
        if (candidates.Count == 0) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var measurements = await Task.WhenAll(candidates.Select(async candidate =>
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, candidate.Url);
                request.Headers.Range = new RangeHeaderValue(0, 0);
                using var response =
                    await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                return (Candidate: candidate,
                    Elapsed: response.IsSuccessStatusCode ? Stopwatch.GetElapsedTime(started) : TimeSpan.MaxValue);
            }
            catch
            {
                return (Candidate: candidate, Elapsed: TimeSpan.MaxValue);
            }
        }));
        return measurements.OrderBy(x => x.Elapsed).First().Candidate;
    }

    public static async Task<JavaRuntimeEntry> InstallAsync(JavaDistributionVersion version, string runtimesPath,
        string temporaryPath, JavaInstallProgressHandler? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(runtimesPath);
        var baseName = SanitizeName($"{version.Vendor}-{version.MajorVersion}");
        var target = GetUniqueDirectory(Path.Combine(runtimesPath, baseName));
        var staging = target + $".{Guid.NewGuid():N}.installing";
        var urlPath = new Uri(version.Url).AbsolutePath;
        var extension = urlPath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            ? ".tar.gz"
            : Path.GetExtension(urlPath);
        if (string.IsNullOrWhiteSpace(extension)) extension = ".zip";
        var archive = Path.Combine(temporaryPath, $"java-{Guid.NewGuid():N}{extension}");
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        try
        {
            await DownloadArchiveAsync(version, archive, progress, cancellationToken);
            if (!string.IsNullOrWhiteSpace(version.Sha256))
            {
                progress?.Invoke(new JavaInstallProgress("校验", null, 0, 0, 0));
                await using var hashStream = File.OpenRead(archive);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, cancellationToken));
                if (!actual.Equals(version.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Java 安装包 SHA-256 校验失败。");
            }

            progress?.Invoke(new JavaInstallProgress("解压", null, 0, 0, 0));
            Directory.CreateDirectory(staging);
            await ExtractAsync(archive, staging, cancellationToken);
            var root = FindRuntimeRoot(staging);
            Directory.Move(root, target);
            var executable = FindJavaExecutable(target);
            var runtime = await JavaRuntimeManager.FromPathAsync(executable, cancellationToken)
                          ?? throw new InvalidDataException("下载的 Java 运行时无法识别。");
            return runtime;
        }
        finally
        {
            TryDelete(archive);
            TryDelete(staging);
        }
    }

    public static async Task<JavaRuntimeEntry?> InstallMojangAsync(int majorVersion, string runtimesPath,
        JavaInstallProgressHandler? progress = null, CancellationToken cancellationToken = default)
    {
        var component = majorVersion switch
        {
            8 => "jre-legacy", 16 => "java-runtime-alpha", 17 => "java-runtime-gamma",
            21 => "java-runtime-delta", 25 => "java-runtime-epsilon", _ => null
        };
        var arm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        var platform = OperatingSystem.IsWindows() ? arm64 ? "windows-arm64" : "windows-x64"
            : OperatingSystem.IsMacOS() ? arm64 ? "mac-os-arm64" : "mac-os"
            : arm64 ? null : "linux";
        if (component is null || platform is null) return null;

        progress?.Invoke(new JavaInstallProgress("获取元数据", null, 0, 0, 0));
        using var index = await GetJsonAsync(MojangRuntimeIndexUrl, cancellationToken);
        if (!index.RootElement.TryGetProperty(platform, out var platformNode) ||
            !platformNode.TryGetProperty(component, out var components) || components.GetArrayLength() == 0)
            return null;
        var manifestUrl = components[0].GetProperty("manifest").GetProperty("url").GetString()!;
        using var manifest = await GetJsonAsync(manifestUrl, cancellationToken);

        var target = GetUniqueDirectory(Path.Combine(runtimesPath, $"Mojang-{majorVersion}"));
        var staging = target + $".{Guid.NewGuid():N}.installing";
        Directory.CreateDirectory(staging);
        var stopwatch = Stopwatch.StartNew();
        long totalBytes = 0;
        long downloadedBytes = 0;
        try
        {
            var files = new List<(string Name, JsonElement Element)>();
            foreach (var entry in manifest.RootElement.GetProperty("files").EnumerateObject())
            {
                var type = entry.Value.GetProperty("type").GetString();
                var path = SafeRuntimePath(staging, entry.Name);
                if (type == "directory")
                {
                    Directory.CreateDirectory(path);
                    continue;
                }

                if (type != "file") continue;
                totalBytes += entry.Value.GetProperty("downloads").GetProperty("raw").GetProperty("size").GetInt64();
                files.Add((entry.Name, entry.Value));
            }

            using var semaphore = new SemaphoreSlim(Math.Max(1, DownloadManager.MaxThread));
            var tasks = new List<Task>(files.Count);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        var path = SafeRuntimePath(staging, file.Name);
                        var raw = file.Element.GetProperty("downloads").GetProperty("raw");
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        await DownloadFileVerifiedAsync(raw.GetProperty("url").GetString()!, path,
                            raw.GetProperty("sha1").GetString()!, raw.GetProperty("size").GetInt64(),
                            received =>
                            {
                                var downloaded = Interlocked.Add(ref downloadedBytes, received);
                                var elapsed = stopwatch.Elapsed.TotalSeconds;
                                progress?.Invoke(new JavaInstallProgress("下载",
                                    totalBytes > 0 ? Math.Clamp((double)downloaded / totalBytes, 0, 1) : null,
                                    downloaded, totalBytes, downloaded / Math.Max(1.0, elapsed)));
                            }, cancellationToken);
                        if (!OperatingSystem.IsWindows() &&
                            file.Element.TryGetProperty("executable", out var executable) &&
                            executable.GetBoolean())
                            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute |
                                                       UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, CancellationToken.None));
            }

            await Task.WhenAll(tasks);
            var finalElapsed = stopwatch.Elapsed.TotalSeconds;
            progress?.Invoke(new JavaInstallProgress("完成", 1, downloadedBytes, totalBytes,
                downloadedBytes / Math.Max(1.0, finalElapsed)));
            Directory.Move(staging, target);
            return await JavaRuntimeManager.FromPathAsync(FindJavaExecutable(target), cancellationToken);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    private static async Task<IReadOnlyList<JavaDistributionVersion>> GetFeedAsync(CancellationToken cancellationToken)
    {
        if (FeedCache is not null) return FeedCache;
        await FeedLock.WaitAsync(cancellationToken);
        try
        {
            if (FeedCache is not null) return FeedCache;
            var bytes = await Client.GetByteArrayAsync(FeedUrl, cancellationToken);
            using var input = new MemoryStream(bytes);
            Stream decoded = input;
            if (bytes.Length > 6 && bytes[0] == 0xFD && bytes[1] == 0x37 && bytes[2] == 0x7A)
                decoded = new XZStream(input);
            using var document = await JsonDocument.ParseAsync(decoded, cancellationToken: cancellationToken);
            var values = new List<JavaDistributionVersion>();
            foreach (var item in document.RootElement.GetProperty("jdks").EnumerateArray())
            {
                var vendor = item.GetProperty("vendor").GetString() ?? "Unknown";
                var product = item.TryGetProperty("product", out var productValue)
                    ? productValue.GetString() ?? ""
                    : "";
                var major = item.GetProperty("jdk_version_major").GetInt32();
                var full = item.GetProperty("jdk_version").GetString() ?? major.ToString();
                if (!item.TryGetProperty("packages", out var packages)) continue;
                foreach (var package in packages.EnumerateArray())
                {
                    if (package.GetProperty("os").GetString() != FeedOs() ||
                        package.GetProperty("arch").GetString() != FeedArch()) continue;
                    values.Add(new JavaDistributionVersion(major, full, vendor, product,
                        package.GetProperty("url").GetString()!, package.GetProperty("sha256").GetString() ?? "",
                        package.GetProperty("archive_size").GetInt64(),
                        package.GetProperty("install_folder_name").GetString() ?? $"java-{major}"));
                    break;
                }
            }

            return FeedCache = values;
        }
        finally
        {
            FeedLock.Release();
        }
    }

    private static async Task DownloadArchiveAsync(JavaDistributionVersion version, string destination,
        JavaInstallProgressHandler? progress, CancellationToken cancellationToken)
    {
        var request = new DownloadRequest(version.Url, destination, version.Size)
        {
            ProgressChanged = e => progress?.Invoke(new JavaInstallProgress("下载",
                e.TotalBytes > 0 ? Math.Clamp((double)e.DownloadedBytes / e.TotalBytes, 0, 1) : null,
                e.DownloadedBytes, e.TotalBytes, e.Speed))
        };
        var result = await new DefaultDownloader().DownloadAsync(request, cancellationToken);
        if (result.Type == DownloadResultType.Cancelled)
            throw new OperationCanceledException(cancellationToken);
        if (result.Type != DownloadResultType.Successful)
            throw result.Exception ?? new IOException("Java 安装包下载失败。");
    }

    private static async Task DownloadFileVerifiedAsync(string url, string destination, string sha1, long size,
        Action<long> progressCallback, CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(1, DownloadManager.MaxRetryCount);
        Exception? lastError = null;
        for (var attempt = 1; attempt <= maxRetries && !cancellationToken.IsCancellationRequested; attempt++)
        {
            try
            {
                if (File.Exists(destination)) File.Delete(destination);
                var request = new DownloadRequest(url, destination, size)
                {
                    ProgressChanged = e => progressCallback(Math.Max(0, e.DownloadedBytes))
                };
                var result =
                    await new DefaultDownloader { MaxRetryCount = 1 }.DownloadAsync(request, cancellationToken);
                if (result.Type == DownloadResultType.Cancelled)
                    throw new OperationCanceledException(cancellationToken);
                if (result.Type != DownloadResultType.Successful)
                    throw result.Exception ?? new IOException("Java 运行时文件下载失败。");
                await using var stream = File.OpenRead(destination);
                var actual = Convert.ToHexString(await SHA1.HashDataAsync(stream, cancellationToken));
                if (actual.Equals(sha1, StringComparison.OrdinalIgnoreCase)) return;
                lastError = new InvalidDataException("Java 运行时文件 SHA-1 校验失败。");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastError = exception;
            }

            if (attempt < maxRetries)
                await Task.Delay(TimeSpan.FromMilliseconds(1000 * attempt), cancellationToken);
        }

        throw lastError ?? new IOException("Java 运行时文件下载失败。");
    }

    private static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        await using var stream = await Client.GetStreamAsync(url, cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string SafeRuntimePath(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Java 清单包含无效路径。");
        return full;
    }

    private static string FeedOs()
    {
        return OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macOS" : "linux";
    }

    private static string FeedArch()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "aarch64",
            _ => "x86_64"
        };
    }

    private static async Task ExtractAsync(string archive, string destination, CancellationToken cancellationToken)
    {
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(archive);
            var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.GetFullPath(Path.Combine(destination,
                    entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Java 压缩包包含无效路径。");
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(path);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await using var output = File.Create(path);
                    await using var input = entry.Open();
                    await input.CopyToAsync(output, cancellationToken);
                }
            }

            return;
        }

        await using var file = File.OpenRead(archive);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gzip, destination, false);
    }

    private static string FindRuntimeRoot(string staging)
    {
        return Directory.EnumerateDirectories(staging).SingleOrDefault() ?? staging;
    }

    private static string FindJavaExecutable(string root)
    {
        var candidates = OperatingSystem.IsWindows() ? new[] { "javaw.exe", "java.exe" } : new[] { "java" };
        var found = candidates.SelectMany(name => Directory.EnumerateFiles(root, name, SearchOption.AllDirectories))
            .FirstOrDefault();
        return found ?? throw new InvalidDataException("Java 安装包中没有找到 Java 可执行文件。");
    }

    private static string GetUniqueDirectory(string path)
    {
        for (var i = 0; Directory.Exists(path); i++)
            path = i == 0 ? path + "-1" : path[..path.LastIndexOf('-')] + $"-{i + 1}";
        return path;
    }

    private static string SanitizeName(string value)
    {
        return string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch
        {
        }
    }
}
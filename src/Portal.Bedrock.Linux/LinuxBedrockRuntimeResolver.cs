using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Portal.Bedrock.Linux;

public sealed record LinuxBedrockRuntime(string ProtonScript, string ProtonRoot, string PrefixPath,
    string SteamClientPath);

public sealed record LinuxBedrockRuntimeProgress(string Message, long BytesReceived = 0, long TotalBytes = 0)
{
    public int Percentage => TotalBytes > 0 ? (int)Math.Min(100, BytesReceived * 100 / TotalBytes) : 0;
}

public sealed class LinuxBedrockRuntimeResolver
{
    public const string ProtonPathVariable = "PORTAL_PROTON_PATH";
    public const string PrefixPathVariable = "PORTAL_BEDROCK_PREFIX";

    private const int DownloadBufferSize = 1024 * 256;
    private static readonly string[] ReleaseApiUrls =
    [
        "https://api.github.com/repos/Weather-OS/GDK-Proton/releases/latest",
        "https://api.github.com/repos/LukasPAH/GDK-Proton-Custom/releases/latest"
    ];

    private static readonly HttpClient DownloadClient = CreateDownloadClient();
    private static readonly SemaphoreSlim InstallLock = new(1, 1);

    public LinuxBedrockRuntime Resolve() => ResolveAsync().GetAwaiter().GetResult();

    public async Task<LinuxBedrockRuntime> ResolveAsync(Action<LinuxBedrockRuntimeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureSupportedPlatform();

        var protonScript = await ResolveProtonScriptAsync(progress, cancellationToken).ConfigureAwait(false);
        var protonRoot = Path.GetDirectoryName(protonScript)!;
        var prefixPath = ResolvePrefixPath();
        var steamClientPath = ResolveSteamClientPath();

        Directory.CreateDirectory(prefixPath);
        return new LinuxBedrockRuntime(protonScript, protonRoot, prefixPath, steamClientPath);
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
            if (File.Exists(configuredScript)) return configuredScript;

            throw new FileNotFoundException(
                $"{ProtonPathVariable} 未指向可用的 Proton 脚本。请将它设置为 proton 文件或包含 proton 文件的目录。",
                configuredScript);
        }

        var discovered = FindSteamProton();
        if (discovered is not null) return discovered;

        var installed = FindInstalledProton();
        if (installed is not null) return installed;

        await InstallLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            installed = FindInstalledProton();
            if (installed is not null) return installed;

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
            .OrderByDescending(path => IsGdkProton(path))
            .ThenByDescending(File.GetLastWriteTimeUtc)
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
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (proton is not null) EnsureExecutable(proton);
        return proton;
    }

    private static async Task<string> DownloadAndInstallAsync(Action<LinuxBedrockRuntimeProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Invoke(new LinuxBedrockRuntimeProgress("正在查询 GDK-Proton x64 release"));
        var release = await GetReleaseAsync(cancellationToken).ConfigureAwait(false);
        var tag = SafePathSegment(release.TagName);
        var installRoot = GetProtonInstallRoot();
        var destination = Path.Combine(installRoot, tag);
        var existing = FindProtonInDirectory(destination);
        if (existing is not null) return existing;

        var cacheDirectory = Path.Combine(GetCacheRoot(), "proton", tag);
        var archivePath = Path.Combine(cacheDirectory, SafePathSegment(release.Asset.Name));
        var expectedHash = ParseGitHubDigest(release.Asset.Digest);
        Directory.CreateDirectory(cacheDirectory);
        Directory.CreateDirectory(installRoot);

        if (!await IsCachedArchiveValidAsync(archivePath, expectedHash, cancellationToken).ConfigureAwait(false))
            await DownloadArchiveAsync(release.Asset.BrowserDownloadUrl, archivePath, expectedHash, progress,
                cancellationToken).ConfigureAwait(false);
        else
            progress?.Invoke(new LinuxBedrockRuntimeProgress($"使用已校验的 Proton 缓存：{release.Asset.Name}"));

        var staging = Path.Combine(installRoot, $".install-{tag}-{Guid.NewGuid():N}");
        try
        {
            progress?.Invoke(new LinuxBedrockRuntimeProgress($"正在验证并解压 GDK-Proton {release.TagName}"));
            Directory.CreateDirectory(staging);
            await ValidateArchiveAsync(archivePath, staging, cancellationToken).ConfigureAwait(false);
            await ExtractArchiveAsync(archivePath, staging, cancellationToken).ConfigureAwait(false);

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
            return proton;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    private static async Task<ProtonRelease> GetReleaseAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var apiUrl in ReleaseApiUrls)
        {
            try
            {
                var release = await DownloadClient.GetFromJsonAsync<GitHubRelease>(apiUrl, cancellationToken)
                    .ConfigureAwait(false) ?? throw new InvalidDataException("GitHub API 返回空响应");
                var asset = release.Assets.FirstOrDefault(IsX64TarGzAsset)
                            ?? throw new InvalidDataException("release 中没有 Linux x64 tar.gz 资产");
                if (string.IsNullOrWhiteSpace(release.TagName) || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
                    throw new InvalidDataException("release 元数据缺少 tag 或下载 URL");
                return new ProtonRelease(release.TagName, asset);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
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
            using var response = await DownloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                DownloadBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[DownloadBufferSize];
            var stopwatch = Stopwatch.StartNew();
            long received = 0;
            var lastPercentage = -5;
            var lastReport = TimeSpan.Zero;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                sha256.AppendData(buffer, 0, read);
                received += read;
                var percentage = total > 0 ? (int)(received * 100 / total) : 0;
                if (percentage >= lastPercentage + 5 || stopwatch.Elapsed - lastReport >= TimeSpan.FromSeconds(2))
                {
                    progress?.Invoke(new LinuxBedrockRuntimeProgress("正在下载 GDK-Proton", received, total));
                    lastPercentage = percentage;
                    lastReport = stopwatch.Elapsed;
                }
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Close();
            var actualHash = Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant();
            if (expectedHash is not null && !string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("GDK-Proton 归档 SHA256 与 GitHub digest 不一致");

            File.Move(temporaryPath, archivePath, true);
            await File.WriteAllTextAsync(GetHashSidecarPath(archivePath), actualHash + "\n", cancellationToken)
                .ConfigureAwait(false);
            progress?.Invoke(new LinuxBedrockRuntimeProgress("GDK-Proton 下载及 SHA256 校验完成", received, total));
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
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
        CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(archivePath);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gzip, extractionRoot, false, cancellationToken).ConfigureAwait(false);
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

    private static string ResolveSteamClientPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(home, ".steam", "root"),
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".local", "share", "Steam"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam")
        };

        return candidates.FirstOrDefault(Directory.Exists) ?? throw new DirectoryNotFoundException(
            "未找到 Steam client 目录。请安装 Steam，并确保 ~/.steam/root 或 ~/.local/share/Steam 可用。");
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

    private static HttpClient CreateDownloadClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Portal-Bedrock-Linux/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private sealed record ProtonRelease(string TagName, GitHubAsset Asset);

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
}

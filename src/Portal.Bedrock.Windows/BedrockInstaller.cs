using System;
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
    private static readonly SemaphoreSlim VersionLoadLock = new(1, 1);
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
        var packagePath = Path.Combine(Path.GetDirectoryName(destination)!, "..", "version_save", $"{request.Version.Id}.insPack");
        packagePath = Path.GetFullPath(packagePath);

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

        foreach (var candidate in GetGdkDownloadUrls(url))
        {
            try
            {
                await DownloadAsync(candidate, packagePath, progress, cancellationToken);
                if (await MatchesMd5Async(packagePath, expectedMd5, cancellationToken)) return;
            }
            catch (HttpRequestException)
            {
                // Try the next Xbox CDN host, matching BedrockBoot's GDK fallback behavior.
            }
        }

        throw new InvalidOperationException("无法下载或验证 GDK 安装包。");
    }

    private static IEnumerable<string> GetGdkDownloadUrls(string url)
    {
        var uri = new Uri(url);
        yield return url;
        foreach (var host in new[] { "assets1.xboxlive.cn", "assets2.xboxlive.cn", "assets1.xboxlive.com", "assets2.xboxlive.com", "xvcf1.xboxlive.com", "xvcf2.xboxlive.com" })
            yield return $"https://{host}{uri.AbsolutePath}";
    }

    private static async Task DownloadAsync(string url, string path, IProgress<BedrockInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true);
        var buffer = new byte[1024 * 128];
        long current = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            current += read;
            progress?.Report(new BedrockInstallProgress(current, total, Path.GetFileName(path), "Downloading"));
        }
    }

    private static async Task<bool> MatchesMd5Async(string path, string expectedMd5, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedMd5)) return false;
        await using var stream = File.OpenRead(path);
        var hash = await MD5.HashDataAsync(stream, cancellationToken);
        return string.Equals(Convert.ToHexString(hash), expectedMd5, StringComparison.OrdinalIgnoreCase);
    }
}

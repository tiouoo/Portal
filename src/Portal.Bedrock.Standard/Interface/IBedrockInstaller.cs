using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Portal.Bedrock.Standard.Manifest;

namespace Portal.Bedrock.Standard.Interface;

public interface IBedrockInstaller
{
    Task<IReadOnlyList<BedrockGdkVersion>> GetGdkVersionsAsync(bool refresh, CancellationToken cancellationToken);
    Task InstallGdkAsync(BedrockOnlineInstallRequest request, IProgress<BedrockInstallProgress>? progress = null);

    async Task<IReadOnlyList<BedrockVersion>> GetVersionsAsync(bool refresh, CancellationToken cancellationToken)
    {
        var versions = await GetGdkVersionsAsync(refresh, cancellationToken);
        var result = new List<BedrockVersion>(versions.Count);
        foreach (var version in versions)
            result.Add(new BedrockVersion(version.Id, version.ReleaseTime, version.IsPreview, BedrockBuildType.GDK));
        return result;
    }

    Task InstallAsync(BedrockInstallRequest request, IProgress<BedrockInstallProgress>? progress = null)
    {
        if (request.Version.BuildType != BedrockBuildType.GDK)
            throw new NotSupportedException("当前平台不支持安装 UWP 基岩版。");
        return InstallGdkAsync(new BedrockOnlineInstallRequest(request.Version, request.DestinationPath,
            request.CancellationToken), progress);
    }
}

public record BedrockGdkVersion(string Id, DateTime ReleaseTime, bool IsPreview)
{
    public string ChannelLabel => IsPreview ? "预览版" : "正式版";
    public virtual string BuildLabel => BedrockBuildType.GDK.ToString();
    public string RelativeReleaseTime => FormatRelativeReleaseTime(ReleaseTime);

    private static string FormatRelativeReleaseTime(DateTime releaseTime)
    {
        var published = releaseTime.Kind == DateTimeKind.Utc ? releaseTime.ToLocalTime() : releaseTime;
        var days = (DateTime.Today - published.Date).Days;
        return days switch
        {
            <= 0 => "今天",
            1 => "昨天",
            < 7 => $"{days} 天前",
            < 14 => "上周",
            < 30 => $"{Math.Max(1, days / 7)} 周前",
            < 365 => $"{Math.Max(1, days / 30)} 个月前",
            < 730 => "去年",
            _ => $"{days / 365} 年前"
        };
    }
}

public sealed record BedrockVersion(
    string Id,
    DateTime ReleaseTime,
    bool IsPreview,
    BedrockBuildType BuildType) : BedrockGdkVersion(Id, ReleaseTime, IsPreview)
{
    public override string BuildLabel => BuildType.ToString();
}

public sealed record BedrockInstallRequest(
    BedrockVersion Version,
    string DestinationPath,
    CancellationToken CancellationToken);

public sealed record BedrockOnlineInstallRequest(
    BedrockGdkVersion Version,
    string DestinationPath,
    CancellationToken CancellationToken);

public sealed record BedrockInstallProgress(long Current, long Total, string Item, string State,
    double Speed = 0, TimeSpan? EstimatedRemaining = null);

public static class BedrockInstallationService
{
    public static IBedrockInstaller? DefaultInstaller { get; set; }
}

public interface IBedrockToolsService
{
    Task<bool> IsWindowsAppSdk18InstalledAsync(CancellationToken cancellationToken = default);
    Task UninstallMinecraftAsync(CancellationToken cancellationToken = default);
}

public static class BedrockToolsService
{
    public static IBedrockToolsService? Default { get; set; }
}

public static class BedrockNetworkConfiguration
{
    public static bool DisableSystemProxy { get; private set; }
    public static string? ProxyServer { get; private set; }
    public static string UserAgent { get; private set; } = "Portal Bedrock GDK Downloader";
    public static bool EnableGithubMirror { get; private set; }
    public static string? GithubMirrorUrl { get; private set; }
    public static bool GithubMirrorDirect { get; private set; }
    public static bool EnableFragmentDownload { get; private set; }
    public static int MaxFragmentCount { get; private set; } = 8;
    public static int MaxRetryCount { get; private set; } = 4;
    public static int Version { get; private set; }

    public static void Configure(bool disableSystemProxy, string? proxyServer, string userAgent,
        bool enableGithubMirror = false, string? githubMirrorUrl = null, bool githubMirrorDirect = false,
        bool enableFragmentDownload = true, int maxFragmentCount = 8, int maxRetryCount = 4)
    {
        DisableSystemProxy = disableSystemProxy;
        ProxyServer = proxyServer;
        UserAgent = userAgent;
        EnableGithubMirror = enableGithubMirror;
        GithubMirrorUrl = githubMirrorUrl;
        GithubMirrorDirect = githubMirrorDirect;
        EnableFragmentDownload = enableFragmentDownload;
        MaxFragmentCount = Math.Clamp(maxFragmentCount, 1, 32);
        MaxRetryCount = Math.Clamp(maxRetryCount, 1, 10);
        Version++;
    }
}

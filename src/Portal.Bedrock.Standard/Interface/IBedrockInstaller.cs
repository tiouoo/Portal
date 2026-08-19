using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Portal.Bedrock.Standard.Manifest;
using Portal.Localization;

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
            throw new NotSupportedException(CommonLanguageManager.Instance.bedrockInstall_uwpUnsupported.CurrentValue());
        return InstallGdkAsync(new BedrockOnlineInstallRequest(request.Version, request.DestinationPath,
            request.CancellationToken), progress);
    }
}

public record BedrockGdkVersion(string Id, DateTime ReleaseTime, bool IsPreview)
{
    public string ChannelLabel => IsPreview
        ? CommonLanguageManager.Instance.bedrockInstall_channelPreview.CurrentValue()
        : CommonLanguageManager.Instance.bedrockInstall_channelRelease.CurrentValue();
    public virtual string BuildLabel => BedrockBuildType.GDK.ToString();
    public string RelativeReleaseTime => FormatRelativeReleaseTime(ReleaseTime);

    private static string FormatRelativeReleaseTime(DateTime releaseTime)
    {
        var published = releaseTime.Kind == DateTimeKind.Utc ? releaseTime.ToLocalTime() : releaseTime;
        var days = (DateTime.Today - published.Date).Days;
        return days switch
        {
            <= 0 => CommonLanguageManager.Instance.relativeTime_today.CurrentValue(),
            1 => CommonLanguageManager.Instance.relativeTime_yesterday.CurrentValue(),
            < 7 => string.Format(CommonLanguageManager.Instance.relativeTime_daysAgo.CurrentValue(), days),
            < 14 => CommonLanguageManager.Instance.bedrockInstall_lastWeek.CurrentValue(),
            < 30 => string.Format(CommonLanguageManager.Instance.relativeTime_weeksAgo.CurrentValue(), Math.Max(1, days / 7)),
            < 365 => string.Format(CommonLanguageManager.Instance.relativeTime_monthsAgo.CurrentValue(), Math.Max(1, days / 30)),
            < 730 => CommonLanguageManager.Instance.relativeTime_lastYear.CurrentValue(),
            _ => string.Format(CommonLanguageManager.Instance.relativeTime_yearsAgo.CurrentValue(), days / 365)
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

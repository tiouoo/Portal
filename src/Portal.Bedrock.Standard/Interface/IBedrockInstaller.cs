using System;
using System.Threading;
using System.Threading.Tasks;

namespace Portal.Bedrock.Standard.Interface;

public interface IBedrockInstaller
{
    Task<IReadOnlyList<BedrockGdkVersion>> GetGdkVersionsAsync(bool refresh, CancellationToken cancellationToken);
    Task InstallGdkAsync(BedrockOnlineInstallRequest request, IProgress<BedrockInstallProgress>? progress = null);
}

public sealed record BedrockGdkVersion(string Id, DateTime ReleaseTime, bool IsPreview)
{
    public string ChannelLabel => IsPreview ? "预览版" : "正式版";
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

public sealed record BedrockOnlineInstallRequest(
    BedrockGdkVersion Version,
    string DestinationPath,
    CancellationToken CancellationToken);

public sealed record BedrockInstallProgress(long Current, long Total, string Item, string State);

public static class BedrockInstallationService
{
    public static IBedrockInstaller? DefaultInstaller { get; set; }
}

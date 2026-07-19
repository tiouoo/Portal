using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Flurl.Http;
using Newtonsoft.Json.Linq;
using Portal.Const;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;

namespace Portal.Module.Update;

public sealed record UpdateAsset(string Name, string DownloadUrl, long Size, string? Sha256);

public sealed record UpdateRelease(string Title, long Sequence, IReadOnlyList<UpdateAsset> Assets);

public static class UpdateChecker
{
    public static async Task<string?> Check(TopLevel? sender, bool noreply = false)
    {
        try
        {
            var release = await GetRelease();
            Logger.Info($"Version {Data.Instance.Version.VersionTitle} Remote title: {release.Title}");
            return IsNewer(release) ? release.Title : "latest";
        }
        catch (FlurlHttpException e)
        {
            if (!noreply && sender is not null)
                Dispatcher.UIThread.Post(() => sender.Notice($"网络请求错误: {e.StatusCode}\n{e.Message}", NotificationType.Error));
        }
        catch (Exception e)
        {
            if (!noreply && sender is not null)
                Dispatcher.UIThread.Post(() => sender.Notice($"检查更新失败\n{e.Message}", NotificationType.Error));
        }

        return null;
    }

    public static async Task<UpdateRelease> GetRelease()
    {
        var channel = NormalizeChannel(Data.UiProperty.OverrideUpdateChannel);
        var apiUrl = $"https://api.github.com/repos/tiouoo/Portal/releases/tags/publish-{channel}";
        Logger.Info($"Checking update for {Data.Instance.Version.VersionTitle} from {apiUrl}");

        var json = JObject.Parse(await apiUrl
            .WithHeader("User-Agent", "Portal-Updater")
            .GetStringAsync());
        var title = json["name"]?.ToString().Trim();
        if (string.IsNullOrEmpty(title)) throw new InvalidOperationException("更新发布缺少版本名称。");

        var assets = json["assets"]?.Children()
            .Select(item => new UpdateAsset(
                item["name"]?.ToString() ?? string.Empty,
                item["browser_download_url"]?.ToString() ?? string.Empty,
                item["size"]?.Value<long>() ?? 0,
                ParseSha256(item["digest"]?.ToString())))
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Name)
                            && Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out var uri)
                            && uri.Scheme == Uri.UriSchemeHttps
                            && (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                                || uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)))
            .ToArray() ?? [];

        return new UpdateRelease(title, ParseSequence(title), assets);
    }

    public static bool IsNewer(UpdateRelease release)
    {
        if (release.Title.Equals(Data.Instance.Version.VersionTitle.Trim(), StringComparison.Ordinal)) return false;
        if (!long.TryParse(Data.Instance.Version.Action, NumberStyles.None, CultureInfo.InvariantCulture, out var current))
            return true;
        return release.Sequence == 0 || release.Sequence > current;
    }

    private static string NormalizeChannel(string channel) => channel.Trim().ToLowerInvariant() switch
    {
        "nightly" => "nightly",
        "commit" => "commit",
        _ => throw new NotSupportedException($"不支持更新通道“{channel}”。")
    };

    private static long ParseSequence(string title)
    {
        foreach (var part in title.Split('-', StringSplitOptions.RemoveEmptyEntries).Reverse())
            if (long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0)
                return value;
        return 0;
    }

    private static string? ParseSha256(string? digest)
    {
        const string prefix = "sha256:";
        if (digest is null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var hash = digest[prefix.Length..];
        return hash.Length == 64 && hash.All(Uri.IsHexDigit) ? hash.ToUpperInvariant() : null;
    }
}

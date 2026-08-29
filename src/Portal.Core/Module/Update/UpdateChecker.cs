using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Flurl.Http;
using MinecraftLaunch.Utilities;
using Portal.Core.Classes;
using Portal.Core.Const;
using Portal.Core.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;

namespace Portal.Core.Module.Update;

public sealed record UpdateAsset(string Name, string DownloadUrl, long Size, string? Sha256);

public sealed record UpdateRelease(string Title, long Sequence, IReadOnlyList<UpdateAsset> Assets,
    string Channel = "release");

public static class UpdateChecker
{
    private const string GithubReleasesUrl = "https://api.github.com/repos/tiouoo/Portal/releases?per_page=100";
    private const string CnbReleasesUrl = "https://api.cnb.cool/tiouo/portal/-/releases?page=1&page_size=100";
    private static readonly Regex StableTagPattern = new(@"^v?(\d+)\.(\d+)\.(\d+)$", RegexOptions.Compiled);

    public static async Task<string?> Check(TopLevel? sender, bool noreply = false)
    {
        try
        {
            var release = await GetRelease();
            Logger.Info($"Version {AppVersionService.Instance.Version.VersionTitle} Remote title: {release.Title}");
            return IsNewer(release) ? release.Title : "latest";
        }
        catch (FlurlHttpException e)
        {
            if (!noreply && sender is not null)
                Dispatcher.UIThread.Post(() =>
                    sender.Notice(string.Format(CommonLanguageManager.Instance.update_networkRequestError.CurrentValue(), e.StatusCode, e.Message), NotificationType.Error));
        }
        catch (Exception e)
        {
            if (!noreply && sender is not null)
                Dispatcher.UIThread.Post(() => sender.Notice(string.Format(CommonLanguageManager.Instance.update_checkFailed.CurrentValue(), e.Message), NotificationType.Error));
        }

        return null;
    }

    public static Task<UpdateRelease> GetRelease()
    {
        return GetRelease(Data.ConfigEntry.UpdateSource, Data.UiProperty.OverrideUpdateChannel);
    }

    public static Task<UpdateRelease> GetRelease(UpdateSource source, string channel)
    {
        return source switch
        {
            UpdateSource.Github => GetGithubRelease(channel),
            UpdateSource.Cnb => GetCnbRelease(),
            _ => throw new NotSupportedException(string.Format(CommonLanguageManager.Instance.update_unsupportedSource.CurrentValue(), source))
        };
    }

    public static async Task<UpdateAsset> ResolveDownloadMetadata(UpdateAsset asset)
    {
        if (asset.Size > 0) return asset;


        using var response = await HttpUtil.Client.GetAsync(asset.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var size = response.Content.Headers.ContentLength;
        if (size is not > 0)
            throw new InvalidDataException(string.Format(CommonLanguageManager.Instance.update_cannotGetAssetSize.CurrentValue(), asset.Name));
        return asset with { Size = size.Value };
    }

    public static bool IsNewer(UpdateRelease release)
    {
        var currentChannel = NormalizeInstalledChannel(AppVersionService.Instance.Version.Type);
        if (!currentChannel.Equals(release.Channel, StringComparison.Ordinal)) return true;
        if (release.Title.Equals(AppVersionService.Instance.Version.VersionTitle.Trim(), StringComparison.Ordinal))
            return false;
        if (!long.TryParse(AppVersionService.Instance.Version.Action, NumberStyles.None, CultureInfo.InvariantCulture,
                out var current))
            return true;
        return release.Sequence == 0 || release.Sequence > current;
    }

    private static async Task<UpdateRelease> GetGithubRelease(string configuredChannel)
    {
        var channel = NormalizeChannel(configuredChannel);
        var apiUrl = channel == "release"
            ? GithubReleasesUrl
            : $"https://api.github.com/repos/tiouoo/Portal/releases/tags/publish-{channel}";
        Logger.Info($"Checking update from GitHub: {apiUrl}");

        var text = await HttpUtil.Request(apiUrl).GetStringAsync();
        using var document = JsonDocument.Parse(text);
        var release = channel == "release"
            ? LatestStableRelease(document.RootElement)
            : document.RootElement;

        return CreateRelease(release, channel, asset => IsHttpsUrl(GetString(asset, "browser_download_url"))
                                               && IsGithubUrl(GetString(asset, "browser_download_url")!),
            asset => new UpdateAsset(
                GetString(asset, "name") ?? string.Empty,
                GetString(asset, "browser_download_url") ?? string.Empty,
                GetInt64(asset, "size") ?? 0,
                ParseSha256(GetString(asset, "digest"))));
    }

    private static async Task<UpdateRelease> GetCnbRelease()
    {
        var token = CredentialsService.CnbUpdateToken;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                string.Format(CommonLanguageManager.Instance.update_cnbSourceNotConfigured.CurrentValue(), CredentialsService.CnbUpdateTokenEnvironmentVariable));

        Logger.Info($"Checking update from CNB: {CnbReleasesUrl}");
        var text = await HttpUtil.Request(CnbReleasesUrl)
            .WithHeader("Authorization", $"Bearer {token}")
            .WithHeader("Accept", "application/vnd.cnb.api+json")
            .GetStringAsync();
        using var document = JsonDocument.Parse(text);
        var release = LatestStableRelease(document.RootElement);
        return CreateRelease(release, "release", asset => IsHttpsUrl(GetString(asset, "browser_download_url")),
            asset => new UpdateAsset(
                GetString(asset, "name") ?? string.Empty,
                GetString(asset, "browser_download_url") ?? string.Empty,
                GetInt64(asset, "size") ?? 0,
                ParseSha256(GetString(asset, "hash_algo"), GetString(asset, "hash_value"))));
    }

    private static UpdateRelease CreateRelease(JsonElement release, string channel, Func<JsonElement, bool> assetFilter,
        Func<JsonElement, UpdateAsset> toAsset)
    {
        var title = GetString(release, "name")?.Trim();
        if (string.IsNullOrEmpty(title)) throw new InvalidOperationException(CommonLanguageManager.Instance.update_releaseMissingTitle.CurrentValue());

        var assets = release.TryGetProperty("assets", out var assetsElement) &&
                     assetsElement.ValueKind == JsonValueKind.Array
            ? assetsElement.EnumerateArray()
                .Where(assetFilter)
                .Select(toAsset)
                .Where(asset => !string.IsNullOrWhiteSpace(asset.Name))
                .ToArray()
            : [];
        return new UpdateRelease(title, ParseSequence(title), assets, channel);
    }

    private static string NormalizeInstalledChannel(string type)
    {
        return type.Trim().ToLowerInvariant() switch
        {
            "release" or "stable" => "release",
            "nightly" => "nightly",
            "commit" or "dev" => "commit",
            _ => "unknown"
        };
    }

    private static JsonElement LatestStableRelease(JsonElement releases)
    {
        if (releases.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException(CommonLanguageManager.Instance.update_noStableRelease.CurrentValue());

        JsonElement? best = null;
        (long Major, long Minor, long Patch)? bestVersion = null;
        foreach (var release in releases.EnumerateArray())
        {
            if (GetBool(release, "draft") == true) continue;
            if (GetBool(release, "prerelease") == true) continue;
            var version = ParseStableTag(GetString(release, "tag_name"));
            if (version is null) continue;
            if (bestVersion is null || IsNewerVersion(version.Value, bestVersion.Value))
            {
                bestVersion = version;
                best = release;
            }
        }

        return best ?? throw new InvalidOperationException(CommonLanguageManager.Instance.update_noStableRelease.CurrentValue());
    }

    private static bool IsNewerVersion((long Major, long Minor, long Patch) candidate,
        (long Major, long Minor, long Patch) current)
    {
        if (candidate.Major != current.Major) return candidate.Major > current.Major;
        if (candidate.Minor != current.Minor) return candidate.Minor > current.Minor;
        return candidate.Patch > current.Patch;
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            _ => null
        };
    }

    private static long? GetInt64(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed)) return parsed;
        return null;
    }

    private static bool? GetBool(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static (long Major, long Minor, long Patch)? ParseStableTag(string? tag)
    {
        var match = StableTagPattern.Match(tag ?? string.Empty);
        if (!match.Success) return null;
        return (long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            long.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
    }

    private static bool IsHttpsUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && uri.Scheme == Uri.UriSchemeHttps;
    }

    private static bool IsGithubUrl(string value)
    {
        var host = new Uri(value).Host;
        return host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeChannel(string channel)
    {
        return channel.Trim().ToLowerInvariant() switch
        {
            "release" or "stable" => "release",
            "nightly" => "nightly",
            "commit" or "dev" => "commit",
            _ => throw new NotSupportedException(string.Format(CommonLanguageManager.Instance.update_unsupportedChannel.CurrentValue(), channel))
        };
    }

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
        return digest is not null && digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? ParseSha256("sha256", digest[prefix.Length..])
            : null;
    }

    private static string? ParseSha256(string? algorithm, string? hash)
    {
        return algorithm?.Equals("sha256", StringComparison.OrdinalIgnoreCase) == true
               && hash?.Length == 64
               && hash.All(Uri.IsHexDigit)
            ? hash.ToUpperInvariant()
            : null;
    }
}

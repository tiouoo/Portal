using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Flurl.Http;
using MinecraftLaunch.Utilities;
using Newtonsoft.Json.Linq;
using Portal.Classes.Enums;
using Portal.Const;
using Portal.Core;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;

namespace Portal.Module.Update;

public sealed record UpdateAsset(string Name, string DownloadUrl, long Size, string? Sha256);

public sealed record UpdateRelease(string Title, long Sequence, IReadOnlyList<UpdateAsset> Assets);

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

    public static Task<UpdateRelease> GetRelease() => Data.ConfigEntry.UpdateSource switch
    {
        UpdateSource.Github => GetGithubRelease(),
        UpdateSource.Cnb => GetCnbRelease(),
        _ => throw new NotSupportedException($"不支持更新源“{Data.ConfigEntry.UpdateSource}”。")
    };

    public static async Task<UpdateAsset> ResolveDownloadMetadata(UpdateAsset asset)
    {
        if (asset.Size > 0) return asset;

        // 部分更新源的 release API 不返回附件 size；GET 会跟随其签名 CDN 重定向，
        // ResponseHeadersRead 不会下载响应正文。
        using var response = await HttpUtil.Client.GetAsync(asset.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var size = response.Content.Headers.ContentLength;
        if (size is not > 0)
            throw new InvalidDataException($"无法获取更新包大小：{asset.Name}");
        return asset with { Size = size.Value };
    }

    public static bool IsNewer(UpdateRelease release)
    {
        if (release.Title.Equals(Data.Instance.Version.VersionTitle.Trim(), StringComparison.Ordinal)) return false;
        if (!long.TryParse(Data.Instance.Version.Action, NumberStyles.None, CultureInfo.InvariantCulture, out var current))
            return true;
        return release.Sequence == 0 || release.Sequence > current;
    }

    private static async Task<UpdateRelease> GetGithubRelease()
    {
        var channel = NormalizeChannel(Data.UiProperty.OverrideUpdateChannel);
        var apiUrl = channel == "release"
            ? GithubReleasesUrl
            : $"https://api.github.com/repos/tiouoo/Portal/releases/tags/publish-{channel}";
        Logger.Info($"Checking update from GitHub: {apiUrl}");

        var text = await HttpUtil.Request(apiUrl).GetStringAsync();
        JToken release;
        if (channel == "release")
        {
            release = LatestStableRelease(JArray.Parse(text));
        }
        else
        {
            release = JObject.Parse(text);
        }

        return CreateRelease(release, asset => IsHttpsUrl(asset["browser_download_url"]?.ToString())
                                              && IsGithubUrl(asset["browser_download_url"]!.ToString()),
            asset => new UpdateAsset(
                asset["name"]?.ToString() ?? string.Empty,
                asset["browser_download_url"]?.ToString() ?? string.Empty,
                asset["size"]?.Value<long>() ?? 0,
                ParseSha256(asset["digest"]?.ToString())));
    }

    private static async Task<UpdateRelease> GetCnbRelease()
    {
        var token = ServiceCredentials.CnbUpdateToken;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                $"CNB 更新源未配置 {ServiceCredentials.CnbUpdateTokenEnvironmentVariable}。请使用包含该变量的正式构建。");

        Logger.Info($"Checking update from CNB: {CnbReleasesUrl}");
        var text = await HttpUtil.Request(CnbReleasesUrl)
            .WithHeader("Authorization", $"Bearer {token}")
            .WithHeader("Accept", "application/vnd.cnb.api+json")
            .GetStringAsync();
        var release = LatestStableRelease(JArray.Parse(text));
        return CreateRelease(release, asset => IsHttpsUrl(asset["browser_download_url"]?.ToString()),
            asset => new UpdateAsset(
                asset["name"]?.ToString() ?? string.Empty,
                asset["browser_download_url"]?.ToString() ?? string.Empty,
                asset["size"]?.Value<long>() ?? 0,
                ParseSha256(asset["hash_algo"]?.ToString(), asset["hash_value"]?.ToString())));
    }

    private static UpdateRelease CreateRelease(JToken release, Func<JToken, bool> assetFilter,
        Func<JToken, UpdateAsset> toAsset)
    {
        var title = release["name"]?.ToString().Trim();
        if (string.IsNullOrEmpty(title)) throw new InvalidOperationException("更新发布缺少版本名称。");

        var assets = release["assets"]?.Children()
            .Where(assetFilter)
            .Select(toAsset)
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Name))
            .ToArray() ?? [];
        return new UpdateRelease(title, ParseSequence(title), assets);
    }

    private static JToken LatestStableRelease(IEnumerable<JToken> releases) => releases
        .Where(release => release["draft"]?.Value<bool>() != true)
        .Where(release => release["prerelease"]?.Value<bool>() != true)
        .Select(release => new { Release = release, Version = ParseStableTag(release["tag_name"]?.ToString()) })
        .Where(item => item.Version is not null)
        .OrderByDescending(item => item.Version!.Value.Major)
        .ThenByDescending(item => item.Version!.Value.Minor)
        .ThenByDescending(item => item.Version!.Value.Patch)
        .Select(item => item.Release)
        .FirstOrDefault() ?? throw new InvalidOperationException("远程仓库中未找到正式版发布。");

    private static (long Major, long Minor, long Patch)? ParseStableTag(string? tag)
    {
        var match = StableTagPattern.Match(tag ?? string.Empty);
        if (!match.Success) return null;
        return (long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            long.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
    }

    private static bool IsHttpsUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
                                                      && uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsGithubUrl(string value)
    {
        var host = new Uri(value).Host;
        return host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeChannel(string channel) => channel.Trim().ToLowerInvariant() switch
    {
        "release" or "stable" => "release",
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

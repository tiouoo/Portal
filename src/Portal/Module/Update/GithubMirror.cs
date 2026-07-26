using System;
using Portal.Const;

namespace Portal.Module.Update;

/// <summary>
/// GitHub 镜像加速地址改写，兼容多种镜像源写法：
/// 纯域名（ghfast.top）、前缀式（https://ghfast.top）、{url} 占位符模板式，
/// 以及粘贴了镜像站示例后缀（https://ghfast.top/https://github.com）的写法。
/// </summary>
public static class GithubMirror
{
    private const string UrlPlaceholder = "{url}";

    public static string Apply(string url)
    {
        if (!Data.ConfigEntry.EnableGithubMirror) return url;
        var mirror = Data.ConfigEntry.GithubMirrorUrl?.Trim();
        if (string.IsNullOrEmpty(mirror) || !IsMirrorable(url)) return url;

        var rewritten = Rewrite(url, mirror);
        // 改写结果不是合法 http(s) 地址时回退直连
        return Uri.TryCreate(rewritten, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? rewritten
            : url;
    }

    // 仅改写 github.com 与 githubusercontent.com 的下载类地址；
    // api.github.com 绝大多数镜像站不支持，保持直连
    private static bool IsMirrorable(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return false;
        if (uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
            return false;
        return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string Rewrite(string url, string mirror)
    {
        if (mirror.Contains(UrlPlaceholder, StringComparison.OrdinalIgnoreCase))
            return ReplacePlaceholder(mirror, url);
        return $"{NormalizePrefix(mirror)}/{url}";
    }

    private static string ReplacePlaceholder(string template, string url)
    {
        var index = template.IndexOf(UrlPlaceholder, StringComparison.OrdinalIgnoreCase);
        return template[..index] + url + template[(index + UrlPlaceholder.Length)..];
    }

    private static string NormalizePrefix(string mirror)
    {
        var prefix = mirror.TrimEnd('/');
        foreach (var suffix in (string[]) ["/https://github.com", "/http://github.com", "/github.com"])
        {
            if (!prefix.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            prefix = prefix[..^suffix.Length];
            break;
        }

        if (!prefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !prefix.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            prefix = "https://" + prefix;
        return prefix.TrimEnd('/');
    }
}

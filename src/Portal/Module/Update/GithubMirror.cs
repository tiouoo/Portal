using System;
using Portal.Classes.Enums;
using Portal.Const;

namespace Portal.Module.Update;

/// <summary>
/// GitHub 镜像加速地址改写，支持两种模式：
/// <list type="bullet">
/// <item>前缀代理式（Prefix）：在原始 URL 前拼接镜像站地址，如 https://ghfast.top/https://github.com/a/b</item>
/// <item>直接访问式（Direct）：替换原始 URL 的域名为镜像站域名，如 https://bgithub.xyz/a/b</item>
/// </list>
/// 此外仍兼容 {url} 占位符模板式写法。
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
        // 占位符模板式优先，兼容历史写法
        if (mirror.Contains(UrlPlaceholder, StringComparison.OrdinalIgnoreCase))
            return ReplacePlaceholder(mirror, url);

        return Data.ConfigEntry.GithubMirrorMode == GithubMirrorMode.Direct
            ? RewriteDirect(url, mirror)
            : RewritePrefix(url, mirror);
    }

    // 直接访问式：替换域名为镜像站域名
    // https://github.com/a/b + bgithub.xyz -> https://bgithub.xyz/a/b
    private static string RewriteDirect(string url, string mirror)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var original)) return url;

        var mirrorUri = ParseMirrorUrl(mirror);
        if (mirrorUri == null) return url;

        var builder = new UriBuilder(original)
        {
            Scheme = mirrorUri.Scheme,
            Host = mirrorUri.Host,
            Port = mirrorUri.IsDefaultPort ? -1 : mirrorUri.Port
        };

        // 保留镜像站的路径前缀（如 https://mirror.com/github 作为前缀）
        var mirrorPath = mirrorUri.AbsolutePath.TrimEnd('/');
        var originalPath = original.AbsolutePath;

        builder.Path = mirrorPath.Length > 0
            ? mirrorPath + (originalPath.StartsWith('/') ? originalPath : "/" + originalPath)
            : originalPath;

        return builder.Uri.AbsoluteUri;
    }

    // 前缀代理式：在原始 URL 前拼接镜像站地址
    // https://github.com/a/b + https://ghfast.top -> https://ghfast.top/https://github.com/a/b
    private static string RewritePrefix(string url, string mirror)
    {
        return $"{NormalizePrefix(mirror)}/{url}";
    }

    // 将用户输入的镜像地址解析为 Uri，支持纯域名写法（如 bgithub.xyz）
    private static Uri? ParseMirrorUrl(string mirror)
    {
        var candidate = mirror.Trim();
        if (!candidate.Contains("://", StringComparison.OrdinalIgnoreCase))
            candidate = "https://" + candidate;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ? uri : null;
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

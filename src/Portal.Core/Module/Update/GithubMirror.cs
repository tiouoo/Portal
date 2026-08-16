using System;
using Portal.Classes.Enums;
using Portal.Const;

namespace Portal.Module.Update;

public static class GithubMirror
{
    private const string UrlPlaceholder = "{url}";

    public static string Apply(string url)
    {
        if (!Data.ConfigEntry.EnableGithubMirror) return url;
        var mirror = Data.ConfigEntry.GithubMirrorUrl?.Trim();
        if (string.IsNullOrEmpty(mirror) || !IsMirrorable(url)) return url;

        var rewritten = Rewrite(url, mirror);
        
        return Uri.TryCreate(rewritten, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? rewritten
            : url;
    }

    
    
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

        return Data.ConfigEntry.GithubMirrorMode == GithubMirrorMode.Direct
            ? RewriteDirect(url, mirror)
            : RewritePrefix(url, mirror);
    }

    
    
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

        
        var mirrorPath = mirrorUri.AbsolutePath.TrimEnd('/');
        var originalPath = original.AbsolutePath;

        builder.Path = mirrorPath.Length > 0
            ? mirrorPath + (originalPath.StartsWith('/') ? originalPath : "/" + originalPath)
            : originalPath;

        return builder.Uri.AbsoluteUri;
    }

    
    
    private static string RewritePrefix(string url, string mirror)
    {
        return $"{NormalizePrefix(mirror)}/{url}";
    }

    
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

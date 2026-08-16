using System.Globalization;
using System.Text.Json;
using Avalonia.Platform;
using MinecraftLaunch.Base.Models.Network;

namespace Portal.Core.Services;

public static class UnlistedVersions
{
    private const string ResourcePath = "avares://Portal/Assets/unlisted-versions.json";
    private const string MidnightMarker = "T24:00:00";

    private static readonly object ResourceLock = new();
    private static IReadOnlyList<VersionManifestEntry>? _versions;

    public static IReadOnlyList<VersionManifestEntry> GetVersions()
    {
        if (_versions is not null) return _versions;
        lock (ResourceLock)
        {
            if (_versions is not null) return _versions;
            _versions = LoadFromResource();
        }
        return _versions;
    }

    public static bool IsUnlistedSource(VersionManifestEntry? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Url)) return false;
        return Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri) &&
            uri.Host.Equals("zkitefly.github.io", StringComparison.OrdinalIgnoreCase);
    }

    public static void MergeInto(IList<VersionManifestEntry> entries)
    {
        if (entries.Count == 0) return;

        var existing = new HashSet<string>(entries.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var version in GetVersions())
        {
            if (existing.Add(version.Id))
                entries.Add(version);
        }

        var sorted = entries.OrderByDescending(x => x.ReleaseTime).ToList();
        entries.Clear();
        foreach (var version in sorted) entries.Add(version);
    }

    private static List<VersionManifestEntry> LoadFromResource()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(ResourcePath));
            using var document = JsonDocument.Parse(stream);
            var versions = new List<VersionManifestEntry>();
            foreach (var item in document.RootElement.GetProperty("versions").EnumerateArray())
            {
                if (TryParseEntry(item, out var entry))
                    versions.Add(entry);
            }
            return versions.OrderByDescending(x => x.ReleaseTime).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static bool TryParseEntry(JsonElement item, out VersionManifestEntry? entry)
    {
        entry = null;
        try
        {
            entry = item.Deserialize(VersionManifestEntryContext.Default.VersionManifestEntry);
            return entry is not null;
        }
        catch (Exception)
        {
        }

        if (!item.TryGetProperty("id", out var idToken) || !item.TryGetProperty("url", out var urlToken))
            return false;
        var id = idToken.GetString();
        var url = urlToken.GetString();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url)) return false;

        var type = item.TryGetProperty("type", out var typeToken) ? typeToken.GetString() : null;
        var time = item.TryGetProperty("time", out var timeToken) ? timeToken.GetString() : null;
        var releaseTime = item.TryGetProperty("releaseTime", out var releaseToken) ? releaseToken.GetString() : null;

        entry = new VersionManifestEntry
        {
            Id = id,
            Url = url,
            Type = string.IsNullOrWhiteSpace(type) ? "snapshot" : type,
            Time = NormalizeTimestamp(time),
            ReleaseTime = NormalizeTimestamp(releaseTime)
        };
        return true;
    }

    private static DateTime NormalizeTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DateTime.UnixEpoch;
        var shifted = value.Contains(MidnightMarker, StringComparison.Ordinal);
        var candidate = shifted ? value.Replace(MidnightMarker, "T00:00:00", StringComparison.Ordinal) : value;
        if (DateTimeOffset.TryParse(candidate, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return (shifted ? parsed.AddDays(1) : parsed).UtcDateTime;
        if (DateTime.TryParse(candidate, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var plain))
            return shifted ? plain.AddDays(1) : plain;
        return DateTime.UnixEpoch;
    }
}
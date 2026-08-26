using System.Text.RegularExpressions;
using System.Globalization;
using Iridium.Enums;
using Iridium.Models.Resources;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Installer;
using Portal.Core.App.Helpers;
using Portal.Core.Classes;
using Portal.Core.Const;
using Portal.Core.Minecraft.Models;
using Portal.Core.Minecraft.Services;
using Portal.Core.Services;
using Portal.Localization;

namespace Portal.Views.Pages.DownloadPages;

internal static class DownloadSearchPersistence
{
    public static DownloadSearchSource ToCoreSource(SearchSource source) => source switch
    {
        SearchSource.Modrinth => DownloadSearchSource.Modrinth,
        SearchSource.All => DownloadSearchSource.All,
        _ => DownloadSearchSource.CurseForge
    };

    public static SearchSource ToUiSource(DownloadSearchSource source) => source switch
    {
        DownloadSearchSource.Modrinth => SearchSource.Modrinth,
        DownloadSearchSource.All => SearchSource.All,
        _ => SearchSource.CurseForge
    };

    public static ResourceSource ToResourceSource(SearchSource source)
    {
        if (source == SearchSource.All && string.IsNullOrWhiteSpace(CredentialsService.CurseForgeApiKey))
            return ResourceSource.Modrinth;
        return source switch
        {
            SearchSource.Modrinth => ResourceSource.Modrinth,
            SearchSource.All => ResourceSource.All,
            _ => ResourceSource.CurseForge
        };
    }

    public static DownloadSearchSort ToCoreSort(SearchSort sort) => sort switch
    {
        SearchSort.Popularity => DownloadSearchSort.Popularity,
        SearchSort.Updated => DownloadSearchSort.Updated,
        SearchSort.Newest => DownloadSearchSort.Newest,
        _ => DownloadSearchSort.Relevance
    };

    public static SearchSort ToUiSort(DownloadSearchSort sort) => sort switch
    {
        DownloadSearchSort.Popularity => SearchSort.Popularity,
        DownloadSearchSort.Updated => SearchSort.Updated,
        DownloadSearchSort.Newest => SearchSort.Newest,
        _ => SearchSort.Relevance
    };

    public static string SourceAbbreviation(ResourceSource source)
    {
        return source switch
        {
            ResourceSource.Modrinth => "Modrinth",
            ResourceSource.CurseForge => "CurseForge",
            _ => string.Empty
        };
    }
}

internal static class ResourceSearchPresentation
{
    public static IReadOnlyList<string> BuildTags(ResourceHit hit)
    {
        var source = DownloadSearchPersistence.SourceAbbreviation(hit.Source);
        return hit.Categories
            .Select(category => category.DisplayName ?? category.Name)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Where(tag => !hit.Loaders.Any(loader =>
                string.Equals(loader.ToString(), tag, StringComparison.OrdinalIgnoreCase)))
            .Prepend(source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
    }

    public static string FormatDownloads(long downloads)
    {
        var culture = LocalizationService.CurrentCulture;
        if (!culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return downloads switch
            {
                >= 1_000_000_000 => $"{downloads / 1_000_000_000d:0.#}B",
                >= 1_000_000 => $"{downloads / 1_000_000d:0.#}M",
                >= 1_000 => $"{downloads / 1_000d:0.#}K",
                _ => downloads.ToString("N0", culture)
            };

        return downloads switch
        {
            >= 100_000_000 => $"{downloads / 100_000_000d:0.#}亿",
            >= 10_000 => $"{downloads / 10_000d:0.#}万",
            >= 1_000 => $"{downloads / 1_000d:0.#}千",
            _ => downloads.ToString("N0", CultureInfo.CurrentCulture)
        };
    }

    public static string FormatMetadata(DateTime timestamp, long downloads)
    {
        var format = CommonLanguageManager.Instance.mod_downloadCount.CurrentValue()
            .Replace("{1:N0}", "{1}", StringComparison.Ordinal);
        return string.Format(format, RelativeTime.Format(timestamp), FormatDownloads(downloads));
    }
}

public readonly record struct MinecraftVersionSortKey(int Major, int Minor, int Patch, int Stage)
    : IComparable<MinecraftVersionSortKey>
{
    public int CompareTo(MinecraftVersionSortKey other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        return result != 0 ? result : Stage.CompareTo(other.Stage);
    }
}

internal static class MinecraftVersionParsing
{
    public static MinecraftVersionSortKey Parse(string value)
    {
        var match = Regex.Match(value, @"^(?<major>\d+)\.(?<minor>\d+)(?:\.(?<patch>\d+))?(?<suffix>.*)$");
        if (!match.Success) return new MinecraftVersionSortKey(-1, -1, -1, -1);
        var suffix = match.Groups["suffix"].Value;
        var stage = string.IsNullOrEmpty(suffix) ? 3 :
            suffix.Contains("rc", StringComparison.OrdinalIgnoreCase) ? 2 :
            suffix.Contains("pre", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        return new MinecraftVersionSortKey(int.Parse(match.Groups["major"].Value),
            int.Parse(match.Groups["minor"].Value),
            match.Groups["patch"].Success ? int.Parse(match.Groups["patch"].Value) : 0, stage);
    }

    public static ResourceSort ToResourceSort(SearchSort sort)
    {
        return sort switch
        {
            SearchSort.Popularity => ResourceSort.Downloads,
            SearchSort.Updated => ResourceSort.Updated,
            SearchSort.Newest => ResourceSort.Newest,
            _ => ResourceSort.Relevance
        };
    }
}

internal static class MinecraftVersionLoader
{
    private static readonly SemaphoreSlim VersionLoadLock = new(1, 1);
    private static Task<IReadOnlyList<VersionManifestEntry>>? _versionLoadTask;

    public static async Task<IReadOnlyList<string>> LoadReleaseVersionsAsync(CancellationToken cancellationToken)
    {
        await VersionLoadLock.WaitAsync(cancellationToken);
        try
        {
            var entries = Data.UiProperty.MinecraftVersionManifestEntries;
            if (_versionLoadTask is { IsCompleted: true, IsCompletedSuccessfully: false })
                _versionLoadTask = null;
            _versionLoadTask ??= entries.Count == 0
                ? LoadReleaseManifestAsync()
                : Task.FromResult<IReadOnlyList<VersionManifestEntry>>(entries);
            var loadedEntries = await _versionLoadTask.WaitAsync(cancellationToken);
            if (entries.Count == 0) entries.AddRange(loadedEntries);
            return entries.Where(x => x.Type == "release").Select(x => x.Id).Distinct()
                .OrderByDescending(MinecraftVersionParsing.Parse)
                .ThenByDescending(x => x, StringComparer.Ordinal)
                .ToList();
        }
        finally
        {
            VersionLoadLock.Release();
        }
    }

    private static async Task<IReadOnlyList<VersionManifestEntry>> LoadReleaseManifestAsync()
    {
        var entries = (await VanillaInstaller.EnumerableMinecraftAsync()).ToList();
        UnlistedVersions.MergeInto(entries);
        return entries;
    }
}

internal static class CancellationTokens
{
    public static void CancelInBackground(CancellationTokenSource cancellation)
    {
        _ = CancelAndDisposeAsync(cancellation);
    }

    private static async Task CancelAndDisposeAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await cancellation.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }
}

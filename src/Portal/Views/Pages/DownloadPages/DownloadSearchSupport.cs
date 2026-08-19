using System.Text.RegularExpressions;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Installer;
using MinecraftLaunch.Components.Provider;
using Portal.Core.Const;
using Portal.Core.Minecraft.Models;
using Portal.Core.Services;

namespace Portal.Views.Pages.DownloadPages;

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

    public static ModrinthSearchIndex ToModrinthSort(SearchSort sort)
    {
        return sort switch
        {
            SearchSort.Popularity => ModrinthSearchIndex.Downloads,
            SearchSort.Updated => ModrinthSearchIndex.DateUpdated,
            SearchSort.Newest => ModrinthSearchIndex.DatePublished,
            _ => ModrinthSearchIndex.Relevance
        };
    }

    public static SortField ToCurseForgeSort(SearchSort sort)
    {
        return sort switch
        {
            SearchSort.Popularity => SortField.Popularity,
            SearchSort.Updated => SortField.LastUpdated,
            SearchSort.Newest => SortField.ReleasedDate,
            _ => SortField.Featured
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

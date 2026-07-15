using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Portal.Classes.Entries;

namespace Portal.Module.AggregatedSearch;

public class Searcher
{
    public static List<AggregatedSearchEntryType> DisplayOrder { get; } =
    [
        AggregatedSearchEntryType.NextLevelSearch,
        AggregatedSearchEntryType.Page,
        AggregatedSearchEntryType.Account,
        AggregatedSearchEntryType.AuthServer,
        AggregatedSearchEntryType.Instance,
    ];

    private static readonly StringComparer ChineseStringComparer = StringComparer.Create(
        CultureInfo.GetCultureInfo("zh-CN"), CompareOptions.None);

    public static List<AggregatedSearchEntry> Search(string query, AggregatedSearchEntryType? type = null)
    {
        Index.Build();
        IEnumerable<AggregatedSearchEntry> entries = Index.IndexedAggregatedSearchEntries;

        if (type.HasValue)
        {
            entries = entries.Where(e => type.Value.HasFlag(e.Type));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var queryLower = query.Trim().ToLowerInvariant();
            entries = entries.Where(e =>
                e.Title.ToLowerInvariant().Contains(queryLower) ||
                e.Description.ToLowerInvariant().Contains(queryLower) ||
                e.TypeDescription.ToLowerInvariant().Contains(queryLower) ||
                e.TitlePinyins.Any(p => p.Contains(queryLower)) ||
                e.TitleFirstLetters.Any(p => p.Contains(queryLower)) ||
                e.DescriptionPinyins.Any(p => p.Contains(queryLower)) ||
                e.DescriptionFirstLetters.Any(p => p.Contains(queryLower)) ||
                e.TypeDescriptionPinyins.Any(p => p.Contains(queryLower)) ||
                e.TypeDescriptionFirstLetters.Any(p => p.Contains(queryLower))
            );
        }

        var result = entries
            .OrderBy(e => GetTypeOrderIndex(e.Type))
            .ThenBy(e => e.Title, ChineseStringComparer)
            .ToList();

        if (!string.IsNullOrWhiteSpace(query) && 
            (!type.HasValue || type.Value.HasFlag(AggregatedSearchEntryType.NextLevelSearch)))
        {
            var trimmedQuery = query.Trim();
            result.Insert(0, new AggregatedSearchEntry
            {
                Type = AggregatedSearchEntryType.NextLevelSearch,
                Title = $@"在 Crossforge 上搜索 ""{trimmedQuery}""",
                Description = "在 Crossforge 平台中搜索",
                IconKey = "Crossforge",
                TypeDescription = "下级搜索",
                Data = ("crossforge", trimmedQuery)
            });
            result.Insert(1, new AggregatedSearchEntry
            {
                Type = AggregatedSearchEntryType.NextLevelSearch,
                Title = $@"在 Modrinth 上搜索 ""{trimmedQuery}""",
                Description = "在 Modrinth 平台中搜索",
                IconKey = "Modrinth",
                TypeDescription = "下级搜索",
                Data = ("modrinth", trimmedQuery)
            });
        }

        return result;
    }

    private static int GetTypeOrderIndex(AggregatedSearchEntryType type)
    {
        var index = DisplayOrder.IndexOf(type);
        return index >= 0 ? index : int.MaxValue;
    }
}
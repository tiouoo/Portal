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
        AggregatedSearchEntryType.RecentPlay,
        AggregatedSearchEntryType.Instance,
        AggregatedSearchEntryType.Account,
        AggregatedSearchEntryType.AuthServer,
        AggregatedSearchEntryType.Page,
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
            var keywords = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(keyword => keyword.ToLowerInvariant());
            entries = entries.Where(entry => keywords.All(keyword =>
                entry.Title.ToLowerInvariant().Contains(keyword) ||
                entry.Description.ToLowerInvariant().Contains(keyword) ||
                entry.TypeDescription.ToLowerInvariant().Contains(keyword) ||
                entry.TitlePinyins.Any(p => p.Contains(keyword)) ||
                entry.TitleFirstLetters.Any(p => p.Contains(keyword)) ||
                entry.DescriptionPinyins.Any(p => p.Contains(keyword)) ||
                entry.DescriptionFirstLetters.Any(p => p.Contains(keyword)) ||
                entry.TypeDescriptionPinyins.Any(p => p.Contains(keyword)) ||
                entry.TypeDescriptionFirstLetters.Any(p => p.Contains(keyword))
            ));
        }

        var result = entries
            .OrderBy(e => GetTypeOrderIndex(e.Type))
            .ThenBy(e => e.Title, ChineseStringComparer)
            .ToList();

        return result;
    }

    private static int GetTypeOrderIndex(AggregatedSearchEntryType type)
    {
        var index = DisplayOrder.IndexOf(type);
        return index >= 0 ? index : int.MaxValue;
    }
}

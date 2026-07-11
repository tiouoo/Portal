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
        AggregatedSearchEntryType.Account,
        AggregatedSearchEntryType.AuthServer,
        AggregatedSearchEntryType.NextLevelSearch
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
                e.Description.ToLowerInvariant().Contains(queryLower)
            );
        }

        return entries
            .OrderBy(e => GetTypeOrderIndex(e.Type))
            .ThenBy(e => e.Title, ChineseStringComparer)
            .ToList();
    }

    private static int GetTypeOrderIndex(AggregatedSearchEntryType type)
    {
        var index = DisplayOrder.IndexOf(type);
        return index >= 0 ? index : int.MaxValue;
    }
}
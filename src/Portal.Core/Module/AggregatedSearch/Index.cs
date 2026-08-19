using System.Collections.ObjectModel;
using System.Reflection;
using Portal.Core.App.Helpers;
using Portal.Core.Classes.Entries;
using Portal.Core.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Services;
using Portal.Localization;

namespace Portal.Core.Module.AggregatedSearch;

public class Index
{
    private static bool _isDirty = true;
    public static ObservableCollection<AggregatedSearchEntry> IndexedAggregatedSearchEntries { get; } = [];

    public static void MarkDirty()
    {
        _isDirty = true;
    }

    public static void Build()
    {
        if (!_isDirty) return;
        _isDirty = false;

        IndexedAggregatedSearchEntries.Clear();

        foreach (var account in Data.ConfigEntry.MinecraftAccounts)
            IndexedAggregatedSearchEntries.Add(WithPinyin(CreateAccountEntry(account)));

        foreach (var authServer in Data.ConfigEntry.AuthServers)
            IndexedAggregatedSearchEntries.Add(WithPinyin(CreateAuthServerEntry(authServer)));

        foreach (var page in GetAllPages()) IndexedAggregatedSearchEntries.Add(WithPinyin(page));

        foreach (var instance in InstanceManager.Instance.Instances)
            IndexedAggregatedSearchEntries.Add(WithPinyin(CreateInstanceEntry(instance)));

        foreach (var target in RecentPlayListService.Instance.Items)
            IndexedAggregatedSearchEntries.Add(WithPinyin(CreateRecentPlayEntry(target)));
    }

    private static AggregatedSearchEntry WithPinyin(AggregatedSearchEntry entry)
    {
        entry.TitlePinyins = PinyinHelper.GetAllPinyins(entry.Title);
        entry.TitleFirstLetters = PinyinHelper.GetAllFirstLetters(entry.Title);
        entry.DescriptionPinyins = PinyinHelper.GetAllPinyins(entry.Description);
        entry.DescriptionFirstLetters = PinyinHelper.GetAllFirstLetters(entry.Description);
        entry.TypeDescriptionPinyins = PinyinHelper.GetAllPinyins(entry.TypeDescription);
        entry.TypeDescriptionFirstLetters = PinyinHelper.GetAllFirstLetters(entry.TypeDescription);
        return entry;
    }

    private static IEnumerable<AggregatedSearchEntry> GetAllPages()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Portal");
        if (assembly is null) yield break;

        foreach (var type in assembly.GetTypes())
        {
            var attr = type.GetCustomAttribute<AggregatedSearchPageAttribute>();
            if (attr == null) continue;

            yield return new AggregatedSearchEntry
            {
                Type = AggregatedSearchEntryType.Page,
                Title = attr.Title,
                Description = attr.Path,
                IconKey = attr.IconKey,
                Data = type,
                TypeDescription = CommonLanguageManager.Instance.aggregatedSearch_page.CurrentValue()
            };
        }
    }

    private static AggregatedSearchEntry CreateInstanceEntry(MinecraftInstance instance)
    {
        return new AggregatedSearchEntry
        {
            Type = AggregatedSearchEntryType.Instance,
            Title = instance.InstanceName,
            Description = $"{instance.FolderName}·{instance.ShortDisplay}",
            IconKey = instance.Type.ToString(),
            Data = instance,
            TypeDescription = CommonLanguageManager.Instance.aggregatedSearch_instance.CurrentValue()
        };
    }

    private static AggregatedSearchEntry CreateRecentPlayEntry(RecentPlayTarget target)
    {
        return new AggregatedSearchEntry
        {
            Type = AggregatedSearchEntryType.RecentPlay,
            Title = target.Name,
            Description = BuildRecentPlayDescription(target),
            IconKey = target.Type.ToString(),
            Data = target,
            TypeDescription = CommonLanguageManager.Instance.aggregatedSearch_recentPlay.CurrentValue()
        };
    }

    private static string BuildRecentPlayDescription(RecentPlayTarget target)
    {
        var prefix = $"{target.Instance.InstanceName}·";
        if (target.Type != RecentPlayTargetType.World || string.IsNullOrWhiteSpace(target.Id))
            return prefix + target.Details;


        var details = target.Details;
        var separator = details.IndexOf('·');
        if (separator < 0) return prefix + $"{details}·{target.Id}";
        separator = details.IndexOf('·', separator + 1);
        return separator < 0
            ? prefix + $"{details}·{target.Id}"
            : prefix + details.Insert(separator + 1, target.Id + "·");
    }

    private static AggregatedSearchEntry CreateAccountEntry(MinecraftAccount account)
    {
        return new AggregatedSearchEntry
        {
            Type = AggregatedSearchEntryType.Account,
            Title = account.Name,
            Description = account.DisplayAccountNote,
            IconKey = account.AccountType.ToString(),
            Data = account,
            TypeDescription = CommonLanguageManager.Instance.aggregatedSearch_gameProfile.CurrentValue()
        };
    }

    private static AggregatedSearchEntry CreateAuthServerEntry(AuthServer authServer)
    {
        return new AggregatedSearchEntry
        {
            Type = AggregatedSearchEntryType.AuthServer,
            Title = authServer.DisplayText,
            Description = authServer.ServerUrl,
            IconKey = authServer.AuthType.ToString(),
            Data = authServer,
            TypeDescription = CommonLanguageManager.Instance.aggregatedSearch_authServer.CurrentValue()
        };
    }
}
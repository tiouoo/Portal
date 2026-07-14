using System.Collections.ObjectModel;
using System.Reflection;
using Portal.Classes.Entries;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;

namespace Portal.Module.AggregatedSearch;

public class Index
{
    public static ObservableCollection<AggregatedSearchEntry> IndexedAggregatedSearchEntries { get; } = [];

    public static void Build()
    {
        IndexedAggregatedSearchEntries.Clear();

        foreach (var account in Data.ConfigEntry.MinecraftAccounts)
        {
            IndexedAggregatedSearchEntries.Add(CreateAccountEntry(account));
        }

        foreach (var authServer in Data.ConfigEntry.AuthServers)
        {
            IndexedAggregatedSearchEntries.Add(CreateAuthServerEntry(authServer));
        }

        foreach (var page in GetAllPages())
        {
            IndexedAggregatedSearchEntries.Add(page);
        }

        foreach (var instance in InstanceManager.Instance.Instances)
        {
            IndexedAggregatedSearchEntries.Add(CreateInstanceEntry(instance));
        }
    }

    private static IEnumerable<AggregatedSearchEntry> GetAllPages()
    {
        var assembly = Assembly.GetExecutingAssembly();

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
                TypeDescription = "页面"
            };
        }
    }

    private static AggregatedSearchEntry CreateInstanceEntry(MinecraftInstance instance)
    {
        return new AggregatedSearchEntry
        {
            Type = AggregatedSearchEntryType.Instance,
            Title = instance.InstanceName,
            Description = $"{instance.FolderName} · {instance.ShortDisplay}",
            IconKey = instance.Type.ToString(),
            Data = instance,
            TypeDescription = "实例"
        };
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
            TypeDescription = "游戏档案"
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
            TypeDescription = "认证服务器"
        };
    }
}
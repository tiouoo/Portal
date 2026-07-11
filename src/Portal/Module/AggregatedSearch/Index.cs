using System.Collections.ObjectModel;
using Portal.Classes.Entries;
using Portal.Const;
using Portal.Core.Minecraft.Account;

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
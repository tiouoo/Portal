using System;

namespace Portal.Classes.Entries;

public class AggregatedSearchEntry
{
    public AggregatedSearchEntryType Type { get; init; }
    public string TypeDescription { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IconKey { get; set; }
    public object? Data { get; set; }
}

[Flags]
public enum AggregatedSearchEntryType
{
    NextLevelSearch = 1 << 0,
    MinecraftAccount = 1 << 1,
    AuthServer = 1 << 2,
    Page = 1 << 3,
    Instance = 1 << 4,
    
    
    All = NextLevelSearch | MinecraftAccount | AuthServer | Page | Instance,
    Account = MinecraftAccount | AuthServer,
}
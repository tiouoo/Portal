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
    
    
    All = NextLevelSearch | MinecraftAccount | AuthServer,
    Account = MinecraftAccount | AuthServer,
}
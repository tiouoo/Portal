namespace Portal.Core.Minecraft.Classes;

public enum RecentPlayTargetType
{
    World,
    Server
}

public sealed record RecentPlayTarget(
    MinecraftInstance Instance,
    RecentPlayTargetType Type,
    string Id,
    string Name,
    string Details,
    DateTime LastPlayedTime,
    string? WorldIconPath = null,
    byte[]? ServerIconData = null,
    string? ServerAddress = null,
    int? ServerPort = null);

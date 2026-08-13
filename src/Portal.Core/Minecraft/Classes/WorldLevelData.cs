namespace Portal.Core.Minecraft.Classes;

public sealed record WorldLevelData(
    int GameMode,
    int Difficulty,
    bool AllowCommands,
    long Seed);

namespace Portal.Core.Minecraft.Classes;

public sealed record WorldSaveInfo(
    string FolderName,
    string FolderPath,
    string? IconPath,
    DateTime CreationTime,
    DateTime LastWriteTime,
    DateTime? LastPlayedTime,
    string? LevelName,
    string? Version,
    long? Seed,
    int? GameMode,
    bool? AllowCommands,
    int PlayerDataCount,
    int DataPackArchiveCount,
    bool IsLocked);

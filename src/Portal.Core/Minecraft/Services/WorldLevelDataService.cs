using fNbt;
using Portal.Core.Minecraft.Classes;

namespace Portal.Core.Minecraft.Services;

public sealed class WorldLevelDataService
{
    public Task<WorldLevelData?> LoadAsync(string worldPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Load(worldPath, cancellationToken), cancellationToken);
    }

    public Task SaveAsync(string worldPath, WorldLevelData data, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Save(worldPath, data, cancellationToken), cancellationToken);
    }

    private static WorldLevelData? Load(string worldPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(worldPath, "level.dat");
        if (!File.Exists(path)) return null;
        var file = new NbtFile();
        file.LoadFromFile(path);
        var data = file.RootTag["Data"] as NbtCompound ?? file.RootTag;
        return new WorldLevelData(
            GetInt(data, "GameType") ?? -1,
            GetInt(data, "Difficulty") ?? -1,
            GetBool(data, "allowCommands") ?? false,
            (data["WorldGenSettings"] is NbtCompound settings
                ? GetLong(settings, "seed")
                : GetLong(data, "RandomSeed")) ?? 0);
    }

    private static void Save(string worldPath, WorldLevelData settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(worldPath, "level.dat");
        if (!File.Exists(path)) throw new FileNotFoundException("未找到 level.dat 文件。", path);
        var file = new NbtFile();
        file.LoadFromFile(path);
        var data = file.RootTag["Data"] as NbtCompound ?? file.RootTag;

        if (data["GameType"] is NbtInt gameType) gameType.Value = settings.GameMode;
        if (data["Difficulty"] is NbtByte difficulty) difficulty.Value = (byte)settings.Difficulty;
        if (data["allowCommands"] is NbtByte allowCommands)
            allowCommands.Value = settings.AllowCommands ? (byte)1 : (byte)0;

        if (data["WorldGenSettings"] is NbtCompound settingsTag)
        {
            if (settingsTag["seed"] is NbtLong seed) seed.Value = settings.Seed;
        }
        else if (data["RandomSeed"] is NbtLong randomSeed)
        {
            randomSeed.Value = settings.Seed;
        }

        file.SaveToFile(path, NbtCompression.GZip);
    }

    private static long? GetLong(NbtCompound parent, string name)
    {
        return parent[name] switch
        {
            NbtLong tag => tag.Value,
            NbtInt tag => tag.Value,
            _ => null
        };
    }

    private static int? GetInt(NbtCompound parent, string name)
    {
        return parent[name] switch
        {
            NbtInt tag => tag.Value,
            NbtByte tag => tag.Value,
            _ => null
        };
    }

    private static bool? GetBool(NbtCompound parent, string name)
    {
        return parent[name] switch
        {
            NbtByte tag => tag.Value != 0,
            _ => null
        };
    }
}
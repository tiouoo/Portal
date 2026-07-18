using fNbt;
using Portal.Core.Minecraft.Classes;

namespace Portal.Core.Minecraft.Services;

public sealed class WorldSaveService
{
    public Task<IReadOnlyList<WorldSaveInfo>> ScanAsync(MinecraftInstance instance,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Scan(instance, cancellationToken), cancellationToken);
    }

    public Task<bool> IsWorldLockedAsync(string worldPath) => Task.Run(() => IsWorldLocked(worldPath));

    private static IReadOnlyList<WorldSaveInfo> Scan(MinecraftInstance instance, CancellationToken cancellationToken)
    {
        if (instance.Type != MinecraftInstanceType.Java)
            return [];

        var savesPath = instance.GetSpecialFolder(MinecraftSpecialFolder.SavesFolder);
        if (!Directory.Exists(savesPath))
            return [];

        return Directory.EnumerateDirectories(savesPath)
            .Select(path => ReadWorld(path, cancellationToken))
            .Where(info => info != null)
            .Cast<WorldSaveInfo>()
            .OrderByDescending(info => info.LastPlayedTime ?? info.LastWriteTime)
            .ThenBy(info => info.FolderName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static WorldSaveInfo? ReadWorld(string worldPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var levelDatPath = Path.Combine(worldPath, "level.dat");
        if (!File.Exists(levelDatPath))
            return null;

        var directory = new DirectoryInfo(worldPath);
        string? levelName = null;
        string? version = null;
        long? seed = null;
        int? gameMode = null;
        bool? allowCommands = null;
        DateTime? lastPlayed = null;

        try
        {
            var nbt = new NbtFile();
            nbt.LoadFromFile(levelDatPath);
            var data = nbt.RootTag["Data"] as NbtCompound ?? nbt.RootTag;
            levelName = GetString(data, "LevelName");
            gameMode = GetInt(data, "GameType");
            allowCommands = GetBool(data, "allowCommands");
            lastPlayed = GetLong(data, "LastPlayed") is { } ticks
                ? DateTimeOffset.FromUnixTimeMilliseconds(ticks).LocalDateTime
                : null;
            version = data["Version"] is NbtCompound versionTag ? GetString(versionTag, "Name") : null;
            seed = data["WorldGenSettings"] is NbtCompound settings ? GetLong(settings, "seed") : GetLong(data, "RandomSeed");
        }
        catch (Exception)
        {
            // A damaged or unsupported level.dat must not hide an otherwise valid world folder.
        }

        var iconPath = Path.Combine(worldPath, "icon.png");
        return new WorldSaveInfo(
            directory.Name,
            worldPath,
            File.Exists(iconPath) ? iconPath : null,
            directory.CreationTime,
            directory.LastWriteTime,
            lastPlayed,
            levelName,
            version,
            seed,
            gameMode,
            allowCommands,
            CountFiles(Path.Combine(worldPath, "playerdata"), "*.dat"),
            CountFiles(Path.Combine(worldPath, "datapacks"), "*.zip"),
            IsWorldLocked(worldPath));
    }

    private static bool IsWorldLocked(string worldPath)
    {
        var lockPath = Path.Combine(worldPath, "session.lock");
        if (!File.Exists(lockPath))
            return false;

        try
        {
            using var stream = new FileStream(lockPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            stream.Lock(0, long.MaxValue);
            stream.Unlock(0, long.MaxValue);
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static int CountFiles(string path, string searchPattern)
    {
        try
        {
            return Directory.Exists(path) ? Directory.EnumerateFiles(path, searchPattern).Count() : 0;
        }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static string? GetString(NbtCompound parent, string name) => (parent[name] as NbtString)?.Value;
    private static long? GetLong(NbtCompound parent, string name) => parent[name] switch
    {
        NbtLong tag => tag.Value,
        NbtInt tag => tag.Value,
        _ => null
    };
    private static int? GetInt(NbtCompound parent, string name) => parent[name] switch
    {
        NbtInt tag => tag.Value,
        NbtByte tag => tag.Value,
        _ => null
    };
    private static bool? GetBool(NbtCompound parent, string name) => parent[name] switch
    {
        NbtByte tag => tag.Value != 0,
        _ => null
    };
}

using fNbt;
using Portal.Core.Minecraft.Classes;

namespace Portal.Core.Minecraft.Services;

public sealed class WorldPlayerDataService
{
    private static readonly string[] PlayerDataRelativePaths = ["players/data", "playerdata"];

    public Task<IReadOnlyList<WorldPlayerData>> LoadAsync(string worldPath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Load(worldPath, cancellationToken), cancellationToken);

    public Task SaveAsync(WorldPlayerData player, CancellationToken cancellationToken = default) =>
        Task.Run(() => Save(player, cancellationToken), cancellationToken);

    public static int CountPlayerDataFiles(string worldPath) => PlayerDataRelativePaths.Sum(relativePath => CountFiles(Path.Combine(worldPath, relativePath)));

    private static IReadOnlyList<WorldPlayerData> Load(string worldPath, CancellationToken cancellationToken)
    {
        var players = new List<WorldPlayerData>();
        foreach (var relativePath in PlayerDataRelativePaths)
        {
            var directory = Path.Combine(worldPath, relativePath);
            if (!Directory.Exists(directory))
                continue;

            foreach (var path in Directory.EnumerateFiles(directory, "*.dat"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var file = new NbtFile();
                    file.LoadFromFile(path);
                    players.Add(ReadPlayer(file.RootTag, path));
                }
                catch (Exception)
                {
                    
                }
            }
        }
        return players.OrderBy(player => player.PlayerId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static WorldPlayerData ReadPlayer(NbtCompound data, string path)
    {
        var abilities = data["abilities"] as NbtCompound;
        var position = data["Pos"] as NbtList;
        return new WorldPlayerData(
            Path.GetFileName(path), path, Path.GetFileNameWithoutExtension(path), GetInt(data, "DataVersion"),
            GetInt(data, "playerGameType"), GetFloat(data, "Health"), GetInt(data, "foodLevel"),
            GetFloat(data, "foodSaturationLevel"), GetInt(data, "XpLevel"), GetInt(data, "XpTotal"),
            GetFloat(data, "XpP"), GetString(data, "Dimension"), GetDouble(position, 0), GetDouble(position, 1), GetDouble(position, 2),
            GetBool(data, "Invulnerable"), GetBool(abilities, "mayfly"), GetBool(abilities, "flying"), GetBool(abilities, "instabuild"));
    }

    private static void Save(WorldPlayerData player, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = new NbtFile();
        file.LoadFromFile(player.FilePath);
        var data = file.RootTag;
        SetInt(data, "playerGameType", player.GameMode);
        SetFloat(data, "Health", player.Health);
        SetInt(data, "foodLevel", player.FoodLevel);
        SetFloat(data, "foodSaturationLevel", player.Saturation);
        SetInt(data, "XpLevel", player.ExperienceLevel);
        SetInt(data, "XpTotal", player.ExperienceTotal);
        SetFloat(data, "XpP", player.ExperienceProgress);
        SetString(data, "Dimension", player.Dimension);
        ReplacePosition(data, player.PositionX, player.PositionY, player.PositionZ);
        SetBool(data, "Invulnerable", player.Invulnerable);
        var abilities = data["abilities"] as NbtCompound ?? new NbtCompound("abilities");
        if (data["abilities"] == null) data.Add(abilities);
        SetBool(abilities, "mayfly", player.MayFly);
        SetBool(abilities, "flying", player.Flying);
        SetBool(abilities, "instabuild", player.Instabuild);
        file.SaveToFile(player.FilePath, NbtCompression.GZip);
    }

    private static int CountFiles(string path)
    {
        try { return Directory.Exists(path) ? Directory.EnumerateFiles(path, "*.dat").Count() : 0; }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static void ReplacePosition(NbtCompound data, double x, double y, double z)
    {
        if (data["Pos"] != null) data.Remove("Pos");
        var position = new NbtList("Pos", NbtTagType.Double);
        position.Add(new NbtDouble(x));
        position.Add(new NbtDouble(y));
        position.Add(new NbtDouble(z));
        data.Add(position);
    }

    private static string GetString(NbtCompound? data, string name) => (data?[name] as NbtString)?.Value ?? "minecraft:overworld";
    private static int GetInt(NbtCompound? data, string name) => (data?[name] as NbtInt)?.Value ?? 0;
    private static float GetFloat(NbtCompound? data, string name) => (data?[name] as NbtFloat)?.Value ?? 0;
    private static double GetDouble(NbtList? data, int index) => data?.ElementAtOrDefault(index) is NbtDouble tag ? tag.Value : 0;
    private static bool GetBool(NbtCompound? data, string name) => data?[name] is NbtByte tag && tag.Value != 0;
    private static void SetInt(NbtCompound data, string name, int value) { if (data[name] is NbtInt tag) tag.Value = value; else { if (data[name] != null) data.Remove(name); data.Add(new NbtInt(name, value)); } }
    private static void SetFloat(NbtCompound data, string name, float value) { if (data[name] is NbtFloat tag) tag.Value = value; else { if (data[name] != null) data.Remove(name); data.Add(new NbtFloat(name, value)); } }
    private static void SetString(NbtCompound data, string name, string value) { if (data[name] is NbtString tag) tag.Value = value; else { if (data[name] != null) data.Remove(name); data.Add(new NbtString(name, value)); } }
    private static void SetBool(NbtCompound data, string name, bool value) { if (data[name] is NbtByte tag) tag.Value = value ? (byte)1 : (byte)0; else { if (data[name] != null) data.Remove(name); data.Add(new NbtByte(name, value ? (byte)1 : (byte)0)); } }
}

using fNbt;
using Portal.Core.Minecraft.Classes;
using Portal.Localization;

namespace Portal.Core.Minecraft.Services;

public sealed class WorldGameRuleService
{
    private const string ModernGameRulesRelativePath = "data/minecraft/game_rules.dat";

    public Task<WorldGameRules?> LoadAsync(string worldPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Load(worldPath, cancellationToken), cancellationToken);
    }

    public Task SaveAsync(string worldPath, WorldGameRules rules, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Save(worldPath, rules, cancellationToken), cancellationToken);
    }

    private static WorldGameRules? Load(string worldPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(worldPath, ModernGameRulesRelativePath);
        var data = File.Exists(path) ? LoadModern(path) : LoadLegacy(worldPath);
        if (data == null)
            return null;

        var booleanRules = new Dictionary<string, bool>(StringComparer.Ordinal);
        var integerRules = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var tag in data.Tags)
        {
            if (string.IsNullOrEmpty(tag.Name))
                continue;

            var name = tag.Name!;

            switch (tag)
            {
                case NbtByte value:
                    booleanRules[name] = value.Value != 0;
                    break;
                case NbtInt value:
                    integerRules[name] = value.Value;
                    break;
                case NbtString value when bool.TryParse(value.Value, out var boolean):
                    booleanRules[name] = boolean;
                    break;
                case NbtString value when int.TryParse(value.Value, out var integer):
                    integerRules[name] = integer;
                    break;
            }
        }

        return new WorldGameRules(booleanRules, integerRules);
    }

    private static void Save(string worldPath, WorldGameRules rules, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(worldPath, ModernGameRulesRelativePath);
        var isModern = File.Exists(path);
        var file = new NbtFile();
        file.LoadFromFile(isModern ? path : Path.Combine(worldPath, "level.dat"));
        var data = isModern
            ? file.RootTag["data"] as NbtCompound
            : (file.RootTag["Data"] as NbtCompound ?? file.RootTag)["GameRules"] as NbtCompound;
        if (data == null) throw new InvalidDataException(CommonLanguageManager.Instance.world_gameRuleNoData.CurrentValue());

        foreach (var (name, value) in rules.BooleanRules)
            if (data[name] is NbtByte tag) tag.Value = value ? (byte)1 : (byte)0;
            else if (data[name] is NbtString stringTag) stringTag.Value = value ? "true" : "false";
        foreach (var (name, value) in rules.IntegerRules)
            if (data[name] is NbtInt tag) tag.Value = value;
            else if (data[name] is NbtString stringTag) stringTag.Value = value.ToString();

        file.SaveToFile(isModern ? path : Path.Combine(worldPath, "level.dat"),
            isModern ? NbtCompression.None : NbtCompression.GZip);
    }

    private static NbtCompound? LoadModern(string path)
    {
        var file = new NbtFile();
        file.LoadFromFile(path);
        return file.RootTag["data"] as NbtCompound;
    }

    private static NbtCompound? LoadLegacy(string worldPath)
    {
        var path = Path.Combine(worldPath, "level.dat");
        if (!File.Exists(path)) return null;
        var file = new NbtFile();
        file.LoadFromFile(path);
        return (file.RootTag["Data"] as NbtCompound ?? file.RootTag)["GameRules"] as NbtCompound;
    }
}
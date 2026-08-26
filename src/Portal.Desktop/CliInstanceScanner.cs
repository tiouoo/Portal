using System.Text.Json;
using Iridium.Models.Minecraft;
using Iridium.Minecraft;
using Iridium.Minecraft.Formats;
using Microsoft.Data.Sqlite;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance.Bedrock;
using Portal.Localization;
using SQLitePCL;

namespace Portal.Desktop;

internal sealed record CliFolderSnapshot(string FolderName, string FolderPath, MinecraftFolderKind FolderKind);

internal sealed record CliInstanceInfo(string Id, string Version, string Loader, string FolderName, string FolderPath);

/// <summary>
/// 轻量级实例扫描，供命令行在不开窗口时读取启动器数据。
/// 与 GUI 扫描共用目录识别逻辑，但不会创建实例配置、不写任何文件。
/// </summary>
internal static class CliInstanceScanner
{
    public static List<CliFolderSnapshot> LoadFolders()
    {
        var settingsPath = Portal.Core.Const.ConfigPath.SettingDataPath;
        if (!File.Exists(settingsPath))
            return [];

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("MinecraftFolders", out var folders) ||
                folders.ValueKind != JsonValueKind.Array)
                return [];

            var result = new List<CliFolderSnapshot>();
            foreach (var item in folders.EnumerateArray())
            {
                var name = GetString(item, "FolderName") ?? string.Empty;
                var path = GetString(item, "FolderPath") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                var kind = GetEnum<MinecraftFolderKind>(item, "FolderKind");
                result.Add(new CliFolderSnapshot(name, path, kind));
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    public static List<CliInstanceInfo> Scan(CliFolderSnapshot folder)
    {
        if (!Directory.Exists(folder.FolderPath))
            return [];

        var layout = DetectLayout(folder);
        return layout.Kind switch
        {
            MinecraftFolderKind.Standard => ScanStandard(layout.RootPath, folder),
            MinecraftFolderKind.PortalMc => ScanPortalMc(layout.RootPath, folder),
            MinecraftFolderKind.MultiMc or MinecraftFolderKind.MultiMcInstance => ScanMultiMc(layout, folder),
            MinecraftFolderKind.CurseForge or MinecraftFolderKind.CurseForgeInstance => ScanCurseForge(layout, folder),
            MinecraftFolderKind.Modrinth or MinecraftFolderKind.ModrinthInstance => ScanModrinth(layout, folder),
            _ => []
        };
    }

    private static MinecraftFolderLayout DetectLayout(CliFolderSnapshot folder)
    {
        var detected = MinecraftFolderLayout.Detect(folder.FolderPath);
        if (detected.Kind == MinecraftFolderKind.Unknown && folder.FolderKind == MinecraftFolderKind.Standard)
            return new MinecraftFolderLayout(MinecraftFolderKind.Standard, folder.FolderPath, folder.FolderPath,
                CommonLanguageManager.Instance.minecraft_traditionalFolder.CurrentValue());
        if (folder.FolderKind is not (MinecraftFolderKind.Auto or MinecraftFolderKind.Standard or
                MinecraftFolderKind.Unknown) &&
            detected.Kind != folder.FolderKind)
            return MinecraftFolderLayout.FromFolderKind(folder.FolderKind, folder.FolderPath);
        return detected;
    }

    private static List<CliInstanceInfo> ScanStandard(string rootPath, CliFolderSnapshot folder)
    {
        var result = new List<CliInstanceInfo>();

        var contexts = new MinecraftProvider(new DirectoryInfo(rootPath), [new StandardMinecraftProvider()])
            .GetMinecraftsAsync().GetAwaiter().GetResult();
        var entries = contexts.Select(context => context.Entry).ToList();
        var baseIds = entries
            .Where(entry => HasClientVersionMetadata(contexts.First(context => context.Entry == entry)) &&
                            entries.Any(other =>
                                string.Equals(other.InheritsFrom, entry.Id, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => entry.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries.Where(entry => !baseIds.Contains(entry.Id)))
        {
            var loader = entry.Loaders.Count > 0
                ? string.Join("+", entry.Loaders.Select(loader => loader.Type.ToString()))
                : CommonLanguageManager.Instance.minecraft_vanilla.CurrentValue();
            result.Add(new CliInstanceInfo(entry.Id, entry.MinecraftVersion, loader, folder.FolderName,
                folder.FolderPath));
        }

        result.AddRange(ScanBedrockVersions(Path.Combine(rootPath, "bedrock_versions"), folder));
        return result;
    }

    private static List<CliInstanceInfo> ScanPortalMc(string rootPath, CliFolderSnapshot folder)
    {
        var result = new List<CliInstanceInfo>();

        var instancesRoot = Path.Combine(rootPath, "instances");
        if (Directory.Exists(instancesRoot))
            foreach (var instanceRoot in Directory.GetDirectories(instancesRoot))
            {
                var id = Path.GetFileName(instanceRoot);
                var instanceJsonPath = Path.Combine(instanceRoot, $"{id}.json");
                if (!File.Exists(instanceJsonPath))
                    continue;

                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(instanceJsonPath));
                    var root = document.RootElement;
                    var inheritsFrom = GetString(root, "inheritsFrom");
                    if (string.IsNullOrWhiteSpace(inheritsFrom))
                        continue;

                    var loader = DetectLoaderFromJson(root)
                                 ?? CommonLanguageManager.Instance.minecraft_vanilla.CurrentValue();
                    result.Add(new CliInstanceInfo(id, inheritsFrom, loader, folder.FolderName, folder.FolderPath));
                }
                catch (JsonException)
                {
                }
                catch (IOException)
                {
                }
            }

        result.AddRange(ScanBedrockVersions(Path.Combine(rootPath, "bedrock_instances"), folder));
        return result;
    }

    private static List<CliInstanceInfo> ScanBedrockVersions(string versionsRoot, CliFolderSnapshot folder)
    {
        var result = new List<CliInstanceInfo>();
        if (!Directory.Exists(versionsRoot))
            return result;

        foreach (var instanceFolder in Directory.GetDirectories(versionsRoot))
        {
            if (!File.Exists(Path.Combine(instanceFolder, "appxmanifest.xml")))
                continue;
            try
            {
                var (version, _) = BedrockHelper.GetInstanceVersion(instanceFolder);
                result.Add(new CliInstanceInfo(Path.GetFileName(instanceFolder), version,
                    CommonLanguageManager.Instance.minecraft_bedrock.CurrentValue(), folder.FolderName,
                    folder.FolderPath));
            }
            catch (Exception)
            {
            }
        }

        return result;
    }

    private static List<CliInstanceInfo> ScanMultiMc(MinecraftFolderLayout layout, CliFolderSnapshot folder)
    {
        var result = new List<CliInstanceInfo>();
        var isRoot = layout.Kind == MinecraftFolderKind.MultiMc;
        var instanceRoots = isRoot && Directory.Exists(Path.Combine(layout.RootPath, "instances"))
            ? Directory.GetDirectories(Path.Combine(layout.RootPath, "instances"))
            : layout.SelectedPath is { Length: > 0 } selected && Directory.Exists(selected)
                ? [selected]
                : [];

        foreach (var instanceRoot in instanceRoots)
        {
            var packPath = Path.Combine(instanceRoot, "mmc-pack.json");
            if (!File.Exists(packPath))
                continue;
            try
            {
                using var pack = JsonDocument.Parse(File.ReadAllText(packPath));
                var root = pack.RootElement;
                if (!root.TryGetProperty("components", out var components) ||
                    components.ValueKind != JsonValueKind.Array)
                    continue;

                var version = string.Empty;
                var loader = string.Empty;
                foreach (var component in components.EnumerateArray())
                {
                    var uid = GetString(component, "uid");
                    if (string.IsNullOrEmpty(uid))
                        continue;
                    if (uid == "net.minecraft")
                        version = GetString(component, "version") ?? string.Empty;
                    else if (string.IsNullOrEmpty(loader))
                        loader = uid;
                }

                if (string.IsNullOrWhiteSpace(version))
                    continue;

                var name = ReadCfgValue(Path.Combine(instanceRoot, "instance.cfg"), "name") ??
                           Path.GetFileName(instanceRoot);
                result.Add(new CliInstanceInfo(name, version,
                    string.IsNullOrEmpty(loader) ? CommonLanguageManager.Instance.minecraft_vanilla.CurrentValue() : loader,
                    folder.FolderName, folder.FolderPath));
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }

        return result;
    }

    private static List<CliInstanceInfo> ScanCurseForge(MinecraftFolderLayout layout, CliFolderSnapshot folder)
    {
        var result = new List<CliInstanceInfo>();
        var isRoot = layout.Kind == MinecraftFolderKind.CurseForge;
        var instanceRoots = isRoot && Directory.Exists(Path.Combine(layout.RootPath, "Instances"))
            ? Directory.GetDirectories(Path.Combine(layout.RootPath, "Instances"))
            : layout.SelectedPath is { Length: > 0 } selected && Directory.Exists(selected)
                ? [selected]
                : [];

        foreach (var instanceRoot in instanceRoots)
        {
            var metadataPath = Path.Combine(instanceRoot, "minecraftinstance.json");
            if (!File.Exists(metadataPath))
                continue;
            try
            {
                using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
                var root = metadata.RootElement;
                if (root.TryGetProperty("isValid", out var valid) && !valid.GetBoolean())
                    continue;
                if (root.TryGetProperty("isEnabled", out var enabled) && !enabled.GetBoolean())
                    continue;

                var gameVersion = GetString(root, "gameVersion") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(gameVersion))
                    continue;

                var loader = string.Empty;
                if (root.TryGetProperty("baseModLoader", out var baseLoader) &&
                    baseLoader.ValueKind == JsonValueKind.Object)
                {
                    var name = GetString(baseLoader, "name");
                    if (!string.IsNullOrWhiteSpace(name))
                        loader = name.Split('-')[0];
                }

                var displayName = GetString(root, "name") ?? Path.GetFileName(instanceRoot);
                result.Add(new CliInstanceInfo(displayName, gameVersion,
                    string.IsNullOrEmpty(loader) ? CommonLanguageManager.Instance.minecraft_vanilla.CurrentValue() : loader,
                    folder.FolderName, folder.FolderPath));
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }

        return result;
    }

    private static List<CliInstanceInfo> ScanModrinth(MinecraftFolderLayout layout, CliFolderSnapshot folder)
    {
        var databasePath = Path.Combine(layout.RootPath, "app.db");
        if (!File.Exists(databasePath))
            return [];

        var result = new List<CliInstanceInfo>();
        try
        {
            Batteries.Init();
            using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT i.path, i.name, c.game_version, c.loader
                                  FROM instances i
                                  JOIN instance_content_sets c ON c.id = i.applied_content_set_id
                                  WHERE i.install_stage = 'installed'
                                  """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.IsDBNull(1) ? reader.GetString(0) : reader.GetString(1);
                var version = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var loader = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                result.Add(new CliInstanceInfo(name, version,
                    string.IsNullOrEmpty(loader) ? CommonLanguageManager.Instance.minecraft_vanilla.CurrentValue() : loader,
                    folder.FolderName, folder.FolderPath));
            }
        }
        catch (Exception)
        {
        }

        return result;
    }

    private static bool HasClientVersionMetadata(MinecraftContext context)
    {
        var entry = context.Entry;
        try
        {
            using var stream = File.OpenRead(IridiumEntryHelper.GetLayout(context).GetVersionJsonPath(entry));
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("clientVersion", out var clientVersion) &&
                   clientVersion.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(clientVersion.GetString());
        }
        catch (IOException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? DetectLoaderFromJson(JsonElement root)
    {
        if (!root.TryGetProperty("libraries", out var libraries) ||
            libraries.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var library in libraries.EnumerateArray())
        {
            if (!library.TryGetProperty("name", out var nameNode) ||
                nameNode.ValueKind != JsonValueKind.String)
                continue;
            var name = nameNode.GetString();
            if (string.IsNullOrEmpty(name))
                continue;
            if (name.Contains("net.minecraftforge:forge:", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("net.minecraftforge:fmlloader:", StringComparison.OrdinalIgnoreCase))
                return "Forge";
            if (name.Contains("net.neoforged.fancymodloader:loader:", StringComparison.OrdinalIgnoreCase))
                return "NeoForge";
            if (name.Contains("optifine:optifine", StringComparison.OrdinalIgnoreCase))
                return "OptiFine";
            if (name.Contains("net.fabricmc:fabric-loader:", StringComparison.OrdinalIgnoreCase))
                return "Fabric";
            if (name.Contains("com.mumfrey:liteloader:", StringComparison.OrdinalIgnoreCase))
                return "LiteLoader";
            if (name.Contains("org.quiltmc:quilt-loader:", StringComparison.OrdinalIgnoreCase))
                return "Quilt";
        }

        return null;
    }

    private static string? ReadCfgValue(string path, string key)
    {
        if (!File.Exists(path))
            return null;
        foreach (var line in File.ReadLines(path))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;
            if (string.Equals(line[..separator].Trim(), key, StringComparison.OrdinalIgnoreCase))
                return line[(separator + 1)..].Trim();
        }

        return null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        return value.GetString();
    }

    private static T GetEnum<T>(JsonElement element, string propertyName) where T : struct, Enum
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                return (T)(object)number;
            if (value.ValueKind == JsonValueKind.String && Enum.TryParse<T>(value.GetString(), true, out var parsed))
                return parsed;
        }

        return default;
    }
}

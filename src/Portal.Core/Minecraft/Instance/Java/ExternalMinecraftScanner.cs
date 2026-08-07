using System.Text.Json;
using Microsoft.Data.Sqlite;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Components.Parser;
using Portal.Core.Minecraft.Classes;

namespace Portal.Core.Minecraft.Instance.Java;

internal static class ExternalMinecraftScanner
{
    public static IReadOnlyList<MinecraftInstance> Scan(MinecraftFolderEntry folder)
    {
        var layout = folder.DetectedLayout;
        return layout.Kind switch
        {
            MinecraftFolderKind.ModrinthApp or MinecraftFolderKind.ModrinthProfile => ScanModrinth(folder, layout,
                MinecraftFolderKind.ModrinthApp),
            MinecraftFolderKind.AxolotlApp or MinecraftFolderKind.AxolotlProfile => ScanModrinth(folder, layout,
                MinecraftFolderKind.AxolotlApp),
            MinecraftFolderKind.MultiMc or MinecraftFolderKind.MultiMcInstance => ScanMultiMc(folder, layout),
            MinecraftFolderKind.BakaXl or MinecraftFolderKind.BakaXlInstance => ScanBakaXl(folder, layout),
            MinecraftFolderKind.CurseForge or MinecraftFolderKind.CurseForgeInstance => ScanCurseForge(folder, layout),
            MinecraftFolderKind.PortalMc => ScanPortalMc(folder, layout),
            _ => []
        };
    }

    private static IReadOnlyList<MinecraftInstance> ScanPortalMc(MinecraftFolderEntry folder,
        MinecraftFolderLayout folderLayout)
    {
        var instancesRoot = Path.Combine(folderLayout.RootPath, "instances");
        if (!Directory.Exists(instancesRoot))
            return [];

        var metadataRoot = Path.Combine(folderLayout.RootPath, "meta");
        var result = new List<MinecraftInstance>();
        foreach (var instanceRoot in Directory.GetDirectories(instancesRoot))
        {
            var instanceId = Path.GetFileName(instanceRoot);
            var instanceJsonPath = Path.Combine(instanceRoot, $"{instanceId}.json");
            if (!File.Exists(instanceJsonPath))
                continue;
            try
            {
                // 实例 json 为极简继承格式，必须声明继承的原版版本 id。
                string? vanillaId;
                bool isReferenceVanilla;
                using (var document = JsonDocument.Parse(File.ReadAllText(instanceJsonPath)))
                {
                    var rootElement = document.RootElement;
                    vanillaId = rootElement.TryGetProperty("inheritsFrom", out var inheritsFromNode)
                        ? inheritsFromNode.GetString()
                        : null;
                    // id 与原版 id 相同时区分“引用原版”（极简 json，libraries 为空）与
                    // 同 id 加载器实例（安装时用临时 id，移动后 json 内层 id 与原版不同）。
                    isReferenceVanilla = !string.IsNullOrEmpty(vanillaId) &&
                                         instanceId.Equals(vanillaId, StringComparison.OrdinalIgnoreCase) &&
                                         rootElement.TryGetProperty("libraries", out var librariesNode) &&
                                         librariesNode.ValueKind == JsonValueKind.Array &&
                                         librariesNode.GetArrayLength() == 0;
                }
                if (string.IsNullOrWhiteSpace(vanillaId))
                    continue;

                var vanillaJsonPath = Path.Combine(metadataRoot, "versions", vanillaId, $"{vanillaId}.json");
                if (!File.Exists(vanillaJsonPath))
                    continue;

                // 原版 json 与实例 json 不在同一根目录，解析前先按实例缓存到临时目录；
                // 实例 id 与原版 id 相同时缓存中仅保留原版 json，即“引用原版”的原版实例。
                var cacheRoot = NormalizePortalMcMetadata(folderLayout.RootPath, instanceId, vanillaId,
                    instanceJsonPath, vanillaJsonPath, isReferenceVanilla);
                var parsed = new MinecraftParser(cacheRoot).GetMinecraft(instanceId);
                var nativesDirectory = Path.Combine(metadataRoot, "natives", instanceId);
                var entry = WithLayout(parsed, instanceId, instanceRoot, metadataRoot,
                    Path.Combine(metadataRoot, "versions", vanillaId), "vanilla", null,
                    Path.Combine(metadataRoot, "versions", vanillaId, $"{vanillaId}.jar"), nativesDirectory);
                // 继承链上的原版入口需一并重映射到 meta，否则其库与资源索引会指向缓存目录。
                entry = WithPortalMcDependencyPaths(entry, metadataRoot, instanceRoot, nativesDirectory);
                var iconPath = ResolveIcon(instanceRoot, "icon.png") ?? ResolveIcon(instanceRoot, "Icon.png")
                    ?? ResolveIcon(instanceRoot, "Portal.Icon.png");
                result.Add(CreateInstance(entry, folder, new MinecraftInstanceLayout(
                    MinecraftFolderKind.PortalMc, folderLayout.RootPath, instanceRoot, instanceRoot, metadataRoot,
                    Path.Combine(metadataRoot, "assets"), Path.Combine(metadataRoot, "libraries"),
                    nativesDirectory, iconPath), instanceId));
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or
                                              ArgumentException or InvalidOperationException)
            {
            }
        }
        return result;
    }

    /// <summary>
    /// 将实例 json 与原版 json 复制到临时目录，使 MinecraftParser 能以统一根目录解析继承链。
    /// </summary>
    private static string NormalizePortalMcMetadata(string root, string instanceId, string vanillaId,
        string instanceJsonPath, string vanillaJsonPath, bool isReferenceVanilla)
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "Portal", "PortalMcMetadata", GetStablePathName(root));
        var vanillaVersionDirectory = Path.Combine(cacheRoot, "versions", vanillaId);
        Directory.CreateDirectory(vanillaVersionDirectory);
        File.Copy(vanillaJsonPath, Path.Combine(vanillaVersionDirectory, $"{vanillaId}.json"), true);

        if (instanceId.Equals(vanillaId, StringComparison.OrdinalIgnoreCase))
        {
            // id 与原版 id 相同时，实例 json 与原版 json 无法在缓存中同名共存。
            // “引用原版”的原版实例直接用缓存中的原版 json；同 id 加载器实例则把缓存副本
            // 的继承目标改写为别名（&lt;instanceId&gt;.vanilla），并以别名目录存放原版 json 供继承解析。
            if (isReferenceVanilla)
                return cacheRoot;

            var instanceVersionDirectory = Path.Combine(cacheRoot, "versions", instanceId);
            Directory.CreateDirectory(instanceVersionDirectory);
            File.WriteAllText(Path.Combine(instanceVersionDirectory, $"{instanceId}.json"),
                RewriteInheritsFrom(instanceJsonPath, $"{instanceId}.vanilla"));

            var vanillaAliasDirectory = Path.Combine(cacheRoot, "versions", $"{instanceId}.vanilla");
            Directory.CreateDirectory(vanillaAliasDirectory);
            File.Copy(vanillaJsonPath, Path.Combine(vanillaAliasDirectory, $"{instanceId}.vanilla.json"), true);
            return cacheRoot;
        }

        var instanceDirectory = Path.Combine(cacheRoot, "versions", instanceId);
        Directory.CreateDirectory(instanceDirectory);
        File.Copy(instanceJsonPath, Path.Combine(instanceDirectory, $"{instanceId}.json"), true);
        return cacheRoot;
    }

    /// <summary>重写缓存副本中的继承目标，用于同 id 加载器实例的继承解析。</summary>
    private static string RewriteInheritsFrom(string instanceJsonPath, string newInheritsFrom)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(instanceJsonPath));
        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("inheritsFrom"))
                    writer.WriteString("inheritsFrom", newInheritsFrom);
                else
                    property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// 将继承链上的原版入口重映射到 meta 目录，使库、资源索引与运行目录均指向共享的 meta。
    /// </summary>
    private static MinecraftEntry WithPortalMcDependencyPaths(MinecraftEntry entry, string metadataRoot,
        string gameDirectory, string nativesDirectory)
    {
        if (entry is not ModifiedMinecraftEntry { HasInheritance: true } modified)
            return entry;

        var inherited = modified.InheritedMinecraft;
        var remappedInherited = new VanillaMinecraftEntry
        {
            Id = inherited.Id,
            Version = inherited.Version,
            ClientJarPath = inherited.ClientJarPath,
            ReleaseTime = inherited.ReleaseTime,
            ClientJsonPath = inherited.ClientJsonPath,
            AssetIndexJsonPath = Path.Combine(metadataRoot, "assets", "indexes",
                Path.GetFileName(inherited.AssetIndexJsonPath)),
            MinecraftFolderPath = metadataRoot,
            // 同 id 加载器实例的缓存副本中继承目录名是别名，真实基座目录须按版本 id 定位。
            VersionDirectoryPath = Path.Combine(metadataRoot, "versions", inherited.Version.VersionId),
            GameDirectoryPath = gameDirectory,
            AssetsDirectoryPath = Path.Combine(metadataRoot, "assets"),
            LibrariesDirectoryPath = Path.Combine(metadataRoot, "libraries"),
            NativesDirectoryPath = nativesDirectory
        };

        return new ModifiedMinecraftEntry
        {
            Id = entry.Id, Version = entry.Version, ClientJarPath = entry.ClientJarPath,
            ReleaseTime = entry.ReleaseTime, ClientJsonPath = entry.ClientJsonPath,
            AssetIndexJsonPath = entry.AssetIndexJsonPath, MinecraftFolderPath = entry.MinecraftFolderPath,
            VersionDirectoryPath = entry.VersionDirectoryPath, GameDirectoryPath = entry.GameDirectoryPath,
            AssetsDirectoryPath = entry.AssetsDirectoryPath, LibrariesDirectoryPath = entry.LibrariesDirectoryPath,
            NativesDirectoryPath = entry.NativesDirectoryPath,
            InheritedMinecraft = remappedInherited,
            ModLoaders = modified.ModLoaders
        };
    }

    private static IReadOnlyList<MinecraftInstance> ScanBakaXl(MinecraftFolderEntry folder,
        MinecraftFolderLayout folderLayout)
    {
        var instanceRoots = folderLayout.Kind == MinecraftFolderKind.BakaXl
            ? Directory.Exists(Path.Combine(folderLayout.RootPath, "instances"))
                ? Directory.GetDirectories(Path.Combine(folderLayout.RootPath, "instances"))
                : []
            : [folderLayout.SelectedPath];
        var result = new List<MinecraftInstance>();
        foreach (var instanceRoot in instanceRoots)
        {
            var packagePath = Path.Combine(instanceRoot, "package.info");
            if (!File.Exists(packagePath)) continue;
            try
            {
                using var package = JsonDocument.Parse(File.ReadAllText(packagePath));
                var components = package.RootElement.GetProperty("components").EnumerateArray().ToArray();
                var minecraft = components.FirstOrDefault(component =>
                    component.TryGetProperty("uid", out var uid) && uid.GetString() == "net.minecraft");
                if (minecraft.ValueKind == JsonValueKind.Undefined) continue;
                var version = minecraft.GetProperty("version").GetString();
                if (string.IsNullOrWhiteSpace(version)) continue;

                var metadataPath = Path.Combine(folderLayout.RootPath, "meta", "net.minecraft", $"{version}.json");
                if (!File.Exists(metadataPath)) continue;
                var normalizedRoot = NormalizeMultiMcMetadata(folderLayout.RootPath, version, metadataPath);
                var parsed = new MinecraftParser(normalizedRoot).GetMinecraft(version);
                var gameDirectory = Path.Combine(instanceRoot, ".minecraft");
                var loaderComponent = components.FirstOrDefault(component =>
                    component.TryGetProperty("uid", out var uid) && uid.GetString() != "net.minecraft");
                var loader = loaderComponent.ValueKind == JsonValueKind.Undefined
                    ? "vanilla"
                    : loaderComponent.GetProperty("uid").GetString() ?? "unknown";
                var loaderVersion = loaderComponent.ValueKind == JsonValueKind.Undefined ||
                                    !loaderComponent.TryGetProperty("version", out var loaderVersionNode)
                    ? null
                    : loaderVersionNode.GetString();
                var entry = WithLayout(parsed, Path.GetFileName(instanceRoot), gameDirectory, normalizedRoot,
                    Path.Combine(normalizedRoot, "versions", version), loader, loaderVersion,
                    GetMultiMcClientJar(folderLayout.RootPath, version), Path.Combine(instanceRoot, "natives"));
                entry = WithDependencyPaths(entry, folderLayout.RootPath, gameDirectory,
                    Path.Combine(instanceRoot, "natives"));
                var name = package.RootElement.TryGetProperty("name", out var nameNode)
                    ? nameNode.GetString() ?? Path.GetFileName(instanceRoot)
                    : Path.GetFileName(instanceRoot);
                var iconPath = ResolveIcon(instanceRoot, "icon.png") ?? ResolveIcon(instanceRoot, "Icon.png");
                result.Add(CreateInstance(entry, folder, new MinecraftInstanceLayout(
                    MinecraftFolderKind.BakaXl, folderLayout.RootPath, instanceRoot, gameDirectory,
                    Path.Combine(folderLayout.RootPath, "meta"), Path.Combine(folderLayout.RootPath, "assets"),
                    Path.Combine(folderLayout.RootPath, "libraries"), Path.Combine(instanceRoot, "natives"), iconPath), name));
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
            {
            }
        }
        return result;
    }

    private static IReadOnlyList<MinecraftInstance> ScanCurseForge(MinecraftFolderEntry folder,
        MinecraftFolderLayout folderLayout)
    {
        var instanceRoots = folderLayout.Kind == MinecraftFolderKind.CurseForge
            ? Directory.Exists(Path.Combine(folderLayout.RootPath, "Instances"))
                ? Directory.GetDirectories(Path.Combine(folderLayout.RootPath, "Instances"))
                : []
            : [folderLayout.SelectedPath];
        var installRoot = Path.Combine(folderLayout.RootPath, "Install");
        var result = new List<MinecraftInstance>();
        foreach (var instanceRoot in instanceRoots)
        {
            var metadataPath = Path.Combine(instanceRoot, "minecraftinstance.json");
            if (!File.Exists(metadataPath)) continue;
            try
            {
                using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
                var root = metadata.RootElement;
                if (root.TryGetProperty("isValid", out var valid) && !valid.GetBoolean()) continue;
                if (root.TryGetProperty("isEnabled", out var enabled) && !enabled.GetBoolean()) continue;
                var gameVersion = root.GetProperty("gameVersion").GetString();
                if (string.IsNullOrWhiteSpace(gameVersion)) continue;
                var versionId = gameVersion;
                string loader = "vanilla";
                string? loaderVersion = null;
                if (root.TryGetProperty("baseModLoader", out var baseLoader) &&
                    baseLoader.ValueKind == JsonValueKind.Object)
                {
                    versionId = baseLoader.TryGetProperty("name", out var nameNode)
                        ? nameNode.GetString() ?? gameVersion
                        : gameVersion;
                    loader = versionId;
                    loaderVersion = baseLoader.TryGetProperty("forgeVersion", out var forgeVersion)
                        ? forgeVersion.GetString()
                        : null;
                }
                var parsed = new MinecraftParser(installRoot).GetMinecraft(versionId);
                var gameDirectory = root.TryGetProperty("installPath", out var installPathNode) &&
                                    !string.IsNullOrWhiteSpace(installPathNode.GetString())
                    ? installPathNode.GetString()!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : instanceRoot;
                var versionDirectory = Path.Combine(installRoot, "versions", versionId);
                var entry = WithLayout(parsed, Path.GetFileName(instanceRoot), gameDirectory, installRoot,
                    versionDirectory, loader, loaderVersion, null, Path.Combine(instanceRoot, "natives"));
                var icon = root.TryGetProperty("profileImagePath", out var iconNode)
                    ? ResolveIcon(instanceRoot, iconNode.GetString() ?? string.Empty)
                    : null;
                icon ??= ResolveIcon(instanceRoot, "icon.png") ?? ResolveIcon(instanceRoot, "Icon.png");
                var displayName = root.TryGetProperty("name", out var displayNameNode)
                    ? displayNameNode.GetString() ?? Path.GetFileName(instanceRoot)
                    : Path.GetFileName(instanceRoot);
                result.Add(CreateInstance(entry, folder, new MinecraftInstanceLayout(
                    MinecraftFolderKind.CurseForge, folderLayout.RootPath, instanceRoot, gameDirectory, installRoot,
                    Path.Combine(installRoot, "assets"), Path.Combine(installRoot, "libraries"),
                    Path.Combine(instanceRoot, "natives"), icon), displayName));
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
            {
            }
        }
        return result;
    }

    private static IReadOnlyList<MinecraftInstance> ScanModrinth(MinecraftFolderEntry folder,
        MinecraftFolderLayout folderLayout, MinecraftFolderKind appKind)
    {
        var databasePath = Path.Combine(folderLayout.RootPath, "app.db");
        if (!File.Exists(databasePath))
            return [];

        try
        {
            SQLitePCL.Batteries.Init();
            using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT i.path, i.name, i.icon_path, c.game_version, c.loader, c.loader_version
                FROM instances i
                JOIN instance_content_sets c ON c.id = i.applied_content_set_id
                WHERE i.install_stage = 'installed'
                """;
            using var reader = command.ExecuteReader();
            var result = new List<MinecraftInstance>();
            while (reader.Read())
            {
                try
                {
                    var profilePath = reader.GetString(0);
                    var gameDirectory = ResolveModrinthProfilePath(folderLayout.RootPath, profilePath);
                    if (folderLayout.Kind is MinecraftFolderKind.ModrinthProfile or MinecraftFolderKind.AxolotlProfile &&
                        !Path.GetFullPath(gameDirectory).Equals(Path.GetFullPath(folderLayout.SelectedPath),
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!Directory.Exists(gameDirectory))
                        continue;

                    var metadataRoot = Path.Combine(folderLayout.RootPath, "meta");
                    var loader = reader.IsDBNull(4) ? "vanilla" : reader.GetString(4);
                    var loaderVersion = reader.IsDBNull(5) ? null : reader.GetString(5);
                    var version = ResolveModrinthVersionId(metadataRoot, reader.GetString(3), loaderVersion);
                    var parsed = new MinecraftParser(metadataRoot).GetMinecraft(version);
                    var entry = WithLayout(parsed, profilePath, gameDirectory, metadataRoot,
                        Path.Combine(metadataRoot, "versions", version),
                        loader, loaderVersion, null, Path.Combine(metadataRoot, "natives", profilePath));
                    var icon = reader.IsDBNull(2) ? null : ResolveIcon(folderLayout.RootPath, reader.GetString(2));
                    icon ??= ResolveIcon(gameDirectory, "icon.png") ?? ResolveIcon(gameDirectory, "Icon.png");
                    result.Add(CreateInstance(entry, folder, new MinecraftInstanceLayout(
                        appKind, folderLayout.RootPath, gameDirectory, gameDirectory, metadataRoot,
                        Path.Combine(metadataRoot, "assets"), Path.Combine(metadataRoot, "libraries"),
                        Path.Combine(metadataRoot, "natives", profilePath), icon), reader.GetString(1)));
                }
                catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or
                                                  ArgumentException or InvalidOperationException)
                {
                }
            }
            return result;
        }
        catch (SqliteException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static IReadOnlyList<MinecraftInstance> ScanMultiMc(MinecraftFolderEntry folder,
        MinecraftFolderLayout folderLayout)
    {
        var instanceRoots = folderLayout.Kind == MinecraftFolderKind.MultiMc
            ? Directory.Exists(Path.Combine(folderLayout.RootPath, "instances"))
                ? Directory.GetDirectories(Path.Combine(folderLayout.RootPath, "instances"))
                : []
            : [folderLayout.SelectedPath];
        var result = new List<MinecraftInstance>();
        foreach (var instanceRoot in instanceRoots)
        {
            var packPath = Path.Combine(instanceRoot, "mmc-pack.json");
            if (!File.Exists(packPath))
                continue;
            try
            {
                using var pack = JsonDocument.Parse(File.ReadAllText(packPath));
                var components = pack.RootElement.GetProperty("components").EnumerateArray().ToArray();
                var minecraft = components.FirstOrDefault(component =>
                    component.TryGetProperty("uid", out var uid) && uid.GetString() == "net.minecraft");
                if (minecraft.ValueKind == JsonValueKind.Undefined)
                    continue;
                var version = minecraft.GetProperty("version").GetString();
                if (string.IsNullOrWhiteSpace(version))
                    continue;

                var metadataPath = Path.Combine(folderLayout.RootPath, "meta", "net.minecraft", $"{version}.json");
                if (!File.Exists(metadataPath))
                    continue;
                var normalizedRoot = NormalizeMultiMcMetadata(folderLayout.RootPath, version, metadataPath);
                var parsed = new MinecraftParser(normalizedRoot).GetMinecraft(version);
                var gameDirectory = Path.Combine(instanceRoot, ".minecraft");
                var loaderComponent = components.FirstOrDefault(component =>
                    component.TryGetProperty("uid", out var uid) && uid.GetString() != "net.minecraft");
                var loader = loaderComponent.ValueKind == JsonValueKind.Undefined
                    ? "vanilla"
                    : loaderComponent.GetProperty("uid").GetString() ?? "unknown";
                var loaderVersion = loaderComponent.ValueKind == JsonValueKind.Undefined ||
                                    !loaderComponent.TryGetProperty("version", out var loaderVersionNode)
                    ? null
                    : loaderVersionNode.GetString();
                var entry = WithLayout(parsed, Path.GetFileName(instanceRoot), gameDirectory, normalizedRoot,
                    Path.Combine(normalizedRoot, "versions", version), loader, loaderVersion,
                    GetMultiMcClientJar(folderLayout.RootPath, version), Path.Combine(instanceRoot, "natives"));
                entry = WithDependencyPaths(entry, folderLayout.RootPath, gameDirectory,
                    Path.Combine(instanceRoot, "natives"));
                var cfg = ReadCfg(Path.Combine(instanceRoot, "instance.cfg"));
                var iconPath = ResolveMultiMcIcon(folderLayout.RootPath, instanceRoot, cfg.GetValueOrDefault("iconKey"));
                var name = cfg.GetValueOrDefault("name") ?? Path.GetFileName(instanceRoot);
                result.Add(CreateInstance(entry, folder, new MinecraftInstanceLayout(
                    MinecraftFolderKind.MultiMc, folderLayout.RootPath, instanceRoot, gameDirectory,
                    Path.Combine(folderLayout.RootPath, "meta"), Path.Combine(folderLayout.RootPath, "assets"),
                    Path.Combine(folderLayout.RootPath, "libraries"), Path.Combine(instanceRoot, "natives"), iconPath), name));
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
            {
            }
        }
        return result;
    }

    private static MinecraftEntry WithDependencyPaths(MinecraftEntry entry, string dependencyRoot,
        string gameDirectory, string nativesDirectory)
    {
        if (entry is ModifiedMinecraftEntry modified)
            return new ModifiedMinecraftEntry
            {
                Id = entry.Id, Version = entry.Version, ClientJarPath = entry.ClientJarPath,
                ReleaseTime = entry.ReleaseTime, ClientJsonPath = entry.ClientJsonPath,
                AssetIndexJsonPath = Path.Combine(dependencyRoot, "assets", "indexes",
                    Path.GetFileName(entry.AssetIndexJsonPath)), MinecraftFolderPath = dependencyRoot,
                VersionDirectoryPath = entry.VersionDirectoryPath, GameDirectoryPath = gameDirectory,
                AssetsDirectoryPath = Path.Combine(dependencyRoot, "assets"),
                LibrariesDirectoryPath = Path.Combine(dependencyRoot, "libraries"),
                NativesDirectoryPath = nativesDirectory, InheritedMinecraft = modified.InheritedMinecraft,
                ModLoaders = modified.ModLoaders
            };
        return new VanillaMinecraftEntry
        {
            Id = entry.Id, Version = entry.Version, ClientJarPath = entry.ClientJarPath,
            ReleaseTime = entry.ReleaseTime, ClientJsonPath = entry.ClientJsonPath,
            AssetIndexJsonPath = Path.Combine(dependencyRoot, "assets", "indexes",
                Path.GetFileName(entry.AssetIndexJsonPath)), MinecraftFolderPath = dependencyRoot,
            VersionDirectoryPath = entry.VersionDirectoryPath, GameDirectoryPath = gameDirectory,
            AssetsDirectoryPath = Path.Combine(dependencyRoot, "assets"),
            LibrariesDirectoryPath = Path.Combine(dependencyRoot, "libraries"),
            NativesDirectoryPath = nativesDirectory
        };
    }

    private static MinecraftInstance CreateInstance(MinecraftEntry entry, MinecraftFolderEntry folder,
        MinecraftInstanceLayout layout, string displayName) => new(entry, layout)
    {
        FolderName = folder.FolderName,
        FolderPath = folder.FolderPath,
        ExternalDisplayName = displayName
    };

    private static MinecraftEntry WithLayout(MinecraftEntry entry, string id, string gameDirectory,
        string metadataRoot, string versionDirectory, string loader, string? loaderVersion, string? clientJar = null,
        string? nativesDirectory = null)
    {
        var common = new
        {
            Id = id,
            entry.Version,
            ClientJarPath = clientJar ?? entry.ClientJarPath,
            entry.ReleaseTime,
            entry.ClientJsonPath,
            AssetIndexJsonPath = Path.Combine(metadataRoot, "assets", "indexes",
                Path.GetFileName(entry.AssetIndexJsonPath)),
            MinecraftFolderPath = metadataRoot,
            VersionDirectoryPath = versionDirectory,
            GameDirectoryPath = gameDirectory,
            AssetsDirectoryPath = Path.Combine(metadataRoot, "assets"),
            LibrariesDirectoryPath = Path.Combine(metadataRoot, "libraries"),
            NativesDirectoryPath = nativesDirectory ?? Path.Combine(versionDirectory, "natives")
        };
        if (entry is ModifiedMinecraftEntry modified)
            return new ModifiedMinecraftEntry
            {
                Id = common.Id, Version = common.Version, ClientJarPath = common.ClientJarPath,
                ReleaseTime = common.ReleaseTime, ClientJsonPath = common.ClientJsonPath,
                AssetIndexJsonPath = common.AssetIndexJsonPath, MinecraftFolderPath = common.MinecraftFolderPath,
                VersionDirectoryPath = common.VersionDirectoryPath, GameDirectoryPath = common.GameDirectoryPath,
                AssetsDirectoryPath = common.AssetsDirectoryPath, LibrariesDirectoryPath = common.LibrariesDirectoryPath,
                NativesDirectoryPath = common.NativesDirectoryPath, InheritedMinecraft = modified.InheritedMinecraft,
                ModLoaders = modified.ModLoaders
            };
        var loaderType = ParseLoader(loader);
        if (loaderType != null)
            return new ModifiedMinecraftEntry
            {
                Id = common.Id, Version = common.Version, ClientJarPath = common.ClientJarPath,
                ReleaseTime = common.ReleaseTime, ClientJsonPath = common.ClientJsonPath,
                AssetIndexJsonPath = common.AssetIndexJsonPath, MinecraftFolderPath = common.MinecraftFolderPath,
                VersionDirectoryPath = common.VersionDirectoryPath, GameDirectoryPath = common.GameDirectoryPath,
                AssetsDirectoryPath = common.AssetsDirectoryPath, LibrariesDirectoryPath = common.LibrariesDirectoryPath,
                NativesDirectoryPath = common.NativesDirectoryPath,
                ModLoaders = [new ModLoaderInfo { Type = loaderType.Value, Version = loaderVersion ?? string.Empty }]
            };
        return new VanillaMinecraftEntry
        {
            Id = common.Id, Version = common.Version, ClientJarPath = common.ClientJarPath,
            ReleaseTime = common.ReleaseTime, ClientJsonPath = common.ClientJsonPath,
            AssetIndexJsonPath = common.AssetIndexJsonPath, MinecraftFolderPath = common.MinecraftFolderPath,
            VersionDirectoryPath = common.VersionDirectoryPath, GameDirectoryPath = common.GameDirectoryPath,
            AssetsDirectoryPath = common.AssetsDirectoryPath, LibrariesDirectoryPath = common.LibrariesDirectoryPath,
            NativesDirectoryPath = common.NativesDirectoryPath
        };
    }

    private static ModLoaderType? ParseLoader(string loader)
    {
        if (loader.Contains("neoforge", StringComparison.OrdinalIgnoreCase)) return ModLoaderType.NeoForge;
        if (loader.Contains("forge", StringComparison.OrdinalIgnoreCase)) return ModLoaderType.Forge;
        if (loader.Contains("fabric", StringComparison.OrdinalIgnoreCase)) return ModLoaderType.Fabric;
        if (loader.Contains("quilt", StringComparison.OrdinalIgnoreCase)) return ModLoaderType.Quilt;
        return null;
    }

    private static string NormalizeMultiMcMetadata(string root, string version, string metadataPath)
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "Portal", "MultiMcMetadata", GetStablePathName(root));
        var versionDirectory = Path.Combine(cacheRoot, "versions", version);
        Directory.CreateDirectory(versionDirectory);
        using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
        var values = document.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone());
        var output = Path.Combine(versionDirectory, $"{version}.json");
        using var stream = File.Create(output);
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        foreach (var (name, value) in values)
        {
            if (name is "version" or "mainJar") continue;
            writer.WritePropertyName(name);
            value.WriteTo(writer);
        }
        writer.WriteString("id", version);
        writer.WriteEndObject();
        return cacheRoot;
    }

    private static string GetStablePathName(string path) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path))))[..16];

    private static string GetMultiMcClientJar(string root, string version) =>
        Path.Combine(root, "libraries", "com", "mojang", "minecraft", version, $"minecraft-{version}-client.jar");

    private static Dictionary<string, string> ReadCfg(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return values;
        // 重复键取最后一次出现的值，避免 ToDictionary 因重复键抛出异常
        foreach (var parts in File.ReadLines(path).Select(line => line.Split('=', 2))
                     .Where(parts => parts.Length == 2))
            values[parts[0]] = parts[1];
        return values;
    }

    private static string? ResolveMultiMcIcon(string root, string instanceRoot, string? iconKey)
    {
        var candidates = new List<string>
        {
            Path.Combine(instanceRoot, ".minecraft", "icon.png"),
            Path.Combine(instanceRoot, ".minecraft", "Icon.png"),
            Path.Combine(instanceRoot, "icon.png")
        };
        if (!string.IsNullOrWhiteSpace(iconKey) && iconKey != "default")
        {
            candidates.Add(Path.Combine(root, "icons", $"{iconKey}.png"));
            candidates.Add(Path.Combine(root, "icons", iconKey));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string ResolveModrinthProfilePath(string root, string profilePath)
    {
        if (Path.IsPathRooted(profilePath))
            return profilePath;

        var profilesRoot = Path.Combine(root, "profiles");
        var directPath = Path.Combine(root, profilePath);
        return Path.GetFullPath(directPath).StartsWith(Path.GetFullPath(profilesRoot) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)
            ? directPath
            : Path.Combine(profilesRoot, profilePath);
    }

    private static string ResolveModrinthVersionId(string metadataRoot, string gameVersion, string? loaderVersion)
    {
        if (!string.IsNullOrWhiteSpace(loaderVersion))
        {
            var loaderVersionId = $"{gameVersion}-{loaderVersion}";
            if (Directory.Exists(Path.Combine(metadataRoot, "versions", loaderVersionId)))
                return loaderVersionId;
        }

        return gameVersion;
    }

    private static string? ResolveIcon(string root, string iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath)) return null;
        var path = Path.IsPathRooted(iconPath) ? iconPath : Path.Combine(root, iconPath);
        return File.Exists(path) ? path : null;
    }
}

using System.IO.Compression;
using System.Text.Json;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance.Bedrock;

namespace Portal.Core.Minecraft.Services;

public enum BedrockPackageArchiveType { Mcpack, Mcaddon, Mcworld }

public enum BedrockPackageContentType { ResourcePack, BehaviorPack, SkinPack, WorldTemplate }

public sealed record BedrockPackageContent(BedrockPackageContentType Type, string Name, string ArchiveRoot);

public sealed record BedrockPackageInspection(BedrockPackageArchiveType ArchiveType, string DisplayName,
    IReadOnlyList<BedrockPackageContent> Contents);

public sealed class BedrockPackageImportService
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public BedrockPackageInspection Inspect(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        var archiveType = GetArchiveType(archivePath);

        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            if (archiveType == BedrockPackageArchiveType.Mcworld)
            {
                var level = archive.Entries.FirstOrDefault(entry =>
                    entry.FullName.EndsWith("level.dat", StringComparison.OrdinalIgnoreCase));
                if (level == null)
                    throw new InvalidDataException("该 MCWORLD 文件不包含 level.dat。");
                return new BedrockPackageInspection(archiveType, Path.GetFileNameWithoutExtension(archivePath), []);
            }

            var contents = archive.Entries
                .Where(entry => entry.FullName.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase))
                .Select(ReadContent)
                .Where(content => content != null)
                .Cast<BedrockPackageContent>()
                .ToArray();
            if (contents.Length == 0)
                throw new InvalidDataException("未找到可导入的基岩版包清单。");

            return new BedrockPackageInspection(archiveType, Path.GetFileNameWithoutExtension(archivePath), contents);
        }
        catch (InvalidDataException) { throw; }
        catch (IOException ex) { throw new InvalidDataException("无法读取基岩版包。", ex); }
    }

    public void Import(string archivePath, BedrockPackageInspection inspection, MinecraftInstance instance, string? userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(instance);
        if (!instance.IsBedrock || instance.BedrockConfig == null)
            throw new InvalidOperationException("请选择一个基岩版实例。");

        using var archive = ZipFile.OpenRead(archivePath);
        if (inspection.ArchiveType == BedrockPackageArchiveType.Mcworld)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new InvalidOperationException("请选择存档用户 ID。");
            var level = archive.Entries.First(entry => entry.FullName.EndsWith("level.dat", StringComparison.OrdinalIgnoreCase));
            var root = GetArchiveDirectory(level.FullName);
            var destinationRoot = BedrockDataPathResolver.GetWorldsFolder(instance.BedrockConfig, userId);
            ExtractDirectory(archive, root, CreateDestination(destinationRoot, inspection.DisplayName), cancellationToken);
            return;
        }

        foreach (var content in inspection.Contents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationRoot = instance.GetSpecialFolder(content.Type switch
            {
                BedrockPackageContentType.ResourcePack => MinecraftSpecialFolder.ResourcePacksFolder,
                BedrockPackageContentType.BehaviorPack => MinecraftSpecialFolder.BehaviorPacksFolder,
                BedrockPackageContentType.SkinPack => MinecraftSpecialFolder.SkinPacksFolder,
                BedrockPackageContentType.WorldTemplate => MinecraftSpecialFolder.WorldTemplatesFolder,
                _ => throw new InvalidOperationException("不支持的基岩版包类型。")
            });
            ExtractDirectory(archive, content.ArchiveRoot, CreateDestination(destinationRoot, content.Name), cancellationToken);
        }
    }

    public static bool TryGetArchiveType(string path, out BedrockPackageArchiveType archiveType)
    {
        archiveType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mcpack" => BedrockPackageArchiveType.Mcpack,
            ".mcaddon" => BedrockPackageArchiveType.Mcaddon,
            ".mcworld" => BedrockPackageArchiveType.Mcworld,
            _ => default
        };
        return Path.GetExtension(path).Equals(".mcpack", StringComparison.OrdinalIgnoreCase) ||
               Path.GetExtension(path).Equals(".mcaddon", StringComparison.OrdinalIgnoreCase) ||
               Path.GetExtension(path).Equals(".mcworld", StringComparison.OrdinalIgnoreCase);
    }

    private static BedrockPackageArchiveType GetArchiveType(string path)
    {
        if (!TryGetArchiveType(path, out var archiveType))
            throw new InvalidDataException("不支持的基岩版包格式。");
        return archiveType;
    }

    private static BedrockPackageContent? ReadContent(ZipArchiveEntry entry)
    {
        try
        {
            using var document = JsonDocument.Parse(entry.Open(), JsonOptions);
            var root = document.RootElement;
            if (!root.TryGetProperty("modules", out var modules) || modules.ValueKind != JsonValueKind.Array)
                return null;

            var types = modules.EnumerateArray().Where(module => module.ValueKind == JsonValueKind.Object)
                .Select(module => module.TryGetProperty("type", out var type) ? type.GetString() : null).ToArray();
            var type = types.Any(value => string.Equals(value, "skin_pack", StringComparison.OrdinalIgnoreCase))
                ? BedrockPackageContentType.SkinPack
                : types.Any(value => string.Equals(value, "world_template", StringComparison.OrdinalIgnoreCase))
                    ? BedrockPackageContentType.WorldTemplate
                    : types.Any(value => string.Equals(value, "data", StringComparison.OrdinalIgnoreCase))
                        ? BedrockPackageContentType.BehaviorPack
                        : types.Any(value => string.Equals(value, "resources", StringComparison.OrdinalIgnoreCase))
                            ? BedrockPackageContentType.ResourcePack
                            : (BedrockPackageContentType?)null;
            if (type == null)
                return null;

            var name = root.TryGetProperty("header", out var header) && header.ValueKind == JsonValueKind.Object &&
                       header.TryGetProperty("name", out var headerName) && headerName.ValueKind == JsonValueKind.String
                ? headerName.GetString()
                : null;
            return new BedrockPackageContent(type.Value, string.IsNullOrWhiteSpace(name) ? "BedrockPack" : name.Trim(),
                GetArchiveDirectory(entry.FullName));
        }
        catch (JsonException) { return null; }
    }

    private static string GetArchiveDirectory(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? string.Empty : path[..separator];
    }

    private static string CreateDestination(string root, string suggestedName)
    {
        Directory.CreateDirectory(root);
        var invalid = Path.GetInvalidFileNameChars();
        var name = string.Concat(suggestedName.Select(character => invalid.Contains(character) ? '_' : character)).Trim();
        if (string.IsNullOrEmpty(name) || name is "." or "..") name = "BedrockPack";
        var destination = Path.Combine(root, name);
        for (var suffix = 2; Directory.Exists(destination); suffix++)
            destination = Path.Combine(root, $"{name} ({suffix})");
        return destination;
    }

    private static void ExtractDirectory(ZipArchive archive, string archiveRoot, string destination, CancellationToken cancellationToken)
    {
        var prefix = string.IsNullOrEmpty(archiveRoot) ? string.Empty : archiveRoot.TrimEnd('/') + "/";
        var entries = archive.Entries.Where(entry => entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (entries.Length == 0)
            throw new InvalidDataException("包中没有可解压的文件。");

        var destinationFullPath = Path.GetFullPath(destination);
        Directory.CreateDirectory(destinationFullPath);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = entry.FullName[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(relativePath)) continue;
            var targetPath = Path.GetFullPath(Path.Combine(destinationFullPath, relativePath));
            if (!targetPath.StartsWith(destinationFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("压缩包包含不安全的文件路径。");
            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath);
        }
    }
}

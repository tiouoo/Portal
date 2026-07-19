using System.Text.Json;
using Portal.Bedrock.Standard.Manifest;
using Portal.Core.Minecraft.Instance.Bedrock;

namespace Portal.Core.Minecraft.Services;

public sealed class BedrockWorldService
{
    public Task<IReadOnlyList<BedrockWorldInfo>> ScanAsync(BedrockInstanceConfig config, string userId,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(config, userId, cancellationToken), cancellationToken);

    private static IReadOnlyList<BedrockWorldInfo> Scan(BedrockInstanceConfig config, string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Directory.EnumerateDirectories(BedrockDataPathResolver.GetWorldsFolder(config, userId))
                .Select(path => Read(path, cancellationToken))
                .Where(world => world != null)
                .Cast<BedrockWorldInfo>()
                .OrderByDescending(world => world.LastWriteTime)
                .ThenBy(world => world.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static BedrockWorldInfo? Read(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(Path.Combine(path, "level.dat"))) return null;

        var directory = new DirectoryInfo(path);
        var folderName = directory.Name;
        var levelName = ReadLevelName(Path.Combine(path, "levelname.txt"));
        return new BedrockWorldInfo(path, folderName, levelName ?? folderName, directory.CreationTime,
            directory.LastWriteTime, ReadFile(Path.Combine(path, "world_icon.jpeg")),
            ReadPackReferences(Path.Combine(path, "world_behavior_packs.json")),
            ReadPackReferences(Path.Combine(path, "world_resource_packs.json")));
    }

    private static string? ReadLevelName(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static byte[]? ReadFile(string path)
    {
        try { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static IReadOnlyList<BedrockWorldPackReference> ReadPackReferences(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream,
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
            return document.RootElement.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.Object)
                .Select(value => new BedrockWorldPackReference(GetString(value, "pack_id"), GetString(value, "subpack"),
                    GetVersion(value, "version")))
                .Where(reference => !string.IsNullOrWhiteSpace(reference.PackId))
                .ToArray();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
        catch (JsonException) { return []; }
    }

    private static string? GetString(JsonElement element, string property) => element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;

    private static string? GetVersion(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.String) return value.GetString()?.Trim();
        if (value.ValueKind != JsonValueKind.Array) return null;
        var version = string.Join('.', value.EnumerateArray().Where(part => part.ValueKind == JsonValueKind.Number)
            .Select(part => part.GetRawText()));
        return string.IsNullOrEmpty(version) ? null : version;
    }
}

public sealed record BedrockWorldInfo(string FolderPath, string FolderName, string DisplayName, DateTime CreationTime,
    DateTime LastWriteTime, byte[]? IconData, IReadOnlyList<BedrockWorldPackReference> BehaviorPacks,
    IReadOnlyList<BedrockWorldPackReference> ResourcePacks);

public sealed record BedrockWorldPackReference(string? PackId, string? Subpack, string? Version);

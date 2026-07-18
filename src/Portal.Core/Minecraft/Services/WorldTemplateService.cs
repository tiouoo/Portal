using System.Text.Json;
using Portal.Core.Minecraft.Classes;

namespace Portal.Core.Minecraft.Services;

public sealed class WorldTemplateService
{
    public Task<IReadOnlyList<WorldTemplateInfo>> ScanAsync(MinecraftInstance instance,
        CancellationToken cancellationToken = default) => Task.Run(() => Scan(instance, cancellationToken), cancellationToken);

    private static IReadOnlyList<WorldTemplateInfo> Scan(MinecraftInstance instance, CancellationToken cancellationToken)
    {
        try
        {
            return Directory.EnumerateDirectories(instance.GetSpecialFolder(MinecraftSpecialFolder.WorldTemplatesFolder))
                .Select(path => Read(path, cancellationToken))
                .Where(template => template != null).Cast<WorldTemplateInfo>()
                .OrderBy(template => template.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(template => template.FileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static WorldTemplateInfo? Read(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifestPath = Path.Combine(path, "manifest.json");
        if (!File.Exists(manifestPath)) return null;

        var directory = new DirectoryInfo(path);
        var fileName = directory.Name;
        var displayName = fileName;
        string? description = null;
        string? version = null;
        string? baseGameVersion = null;
        string? uuid = null;
        var modules = new List<string>();

        try
        {
            using var stream = File.OpenRead(manifestPath);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = document.RootElement;
            if (!root.TryGetProperty("header", out var header) || header.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("modules", out var moduleValues) || moduleValues.ValueKind != JsonValueKind.Array)
                return null;

            var templateModules = moduleValues.EnumerateArray().Where(module => module.ValueKind == JsonValueKind.Object &&
                string.Equals(GetString(module, "type"), "world_template", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (templateModules.Length == 0) return null;

            displayName = GetString(header, "name") ?? displayName;
            description = GetString(header, "description");
            version = GetVersion(header, "version");
            baseGameVersion = GetVersion(header, "base_game_version");
            uuid = GetString(header, "uuid");
            modules.AddRange(templateModules.Select(module => GetString(module, "uuid")).OfType<string>());
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }

        return new WorldTemplateInfo(path, fileName, displayName, description, version, baseGameVersion, uuid, modules,
            ReadIcon(Path.Combine(path, "world_icon.jpeg")), ReadPackReferences(Path.Combine(path, "world_behavior_packs.json")),
            ReadPackReferences(Path.Combine(path, "world_resource_packs.json")));
    }

    private static IReadOnlyList<WorldTemplatePackReference> ReadPackReferences(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
            return document.RootElement.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.Object)
                .Select(value => new WorldTemplatePackReference(GetString(value, "pack_id"), GetString(value, "subpack"), GetVersion(value, "version")))
                .Where(reference => !string.IsNullOrWhiteSpace(reference.PackId)).ToArray();
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
        var version = string.Join('.', value.EnumerateArray().Where(part => part.ValueKind == JsonValueKind.Number).Select(part => part.GetRawText()));
        return string.IsNullOrEmpty(version) ? null : version;
    }

    private static byte[]? ReadIcon(string path)
    {
        try { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}

public sealed record WorldTemplateInfo(string FilePath, string FileName, string DisplayName, string? Description,
    string? Version, string? BaseGameVersion, string? Uuid, IReadOnlyList<string> ModuleUuids, byte[]? IconData,
    IReadOnlyList<WorldTemplatePackReference> BehaviorPacks, IReadOnlyList<WorldTemplatePackReference> ResourcePacks);

public sealed record WorldTemplatePackReference(string? PackId, string? Subpack, string? Version);

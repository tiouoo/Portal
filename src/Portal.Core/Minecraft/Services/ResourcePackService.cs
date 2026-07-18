using System.IO.Compression;
using System.Text.Json;
using Portal.Core.Minecraft.Classes;

namespace Portal.Core.Minecraft.Services;

public sealed class ResourcePackService
{
    public Task<IReadOnlyList<ResourcePackInfo>> ScanAsync(MinecraftInstance instance,
        MinecraftSpecialFolder folder = MinecraftSpecialFolder.ResourcePacksFolder,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(instance, folder, cancellationToken), cancellationToken);

    private static IReadOnlyList<ResourcePackInfo> Scan(MinecraftInstance instance, MinecraftSpecialFolder folder,
        CancellationToken cancellationToken)
    {
        try
        {
            var root = instance.GetSpecialFolder(folder);
            var packs = instance.Type == MinecraftInstanceType.Java
                ? Directory.EnumerateFiles(root, "*.zip").Select(path => ReadJavaPack(path, cancellationToken))
                : Directory.EnumerateDirectories(root)
                    .Where(path => File.Exists(Path.Combine(path, "manifest.json")))
                    .Select(path => ReadBedrockPack(path, cancellationToken));
            return packs
                .OrderBy(pack => pack.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pack => pack.FileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static ResourcePackInfo ReadJavaPack(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = new FileInfo(path);
        var fileName = Path.GetFileName(path);
        var displayName = Path.GetFileNameWithoutExtension(path);
        string? description = null;
        string? supportedFormats = null;
        byte[]? iconData = null;

        try
        {
            using var archive = ZipFile.OpenRead(path);
            var metadata = archive.Entries.FirstOrDefault(entry =>
                entry.FullName.Equals("pack.mcmeta", StringComparison.OrdinalIgnoreCase));
            if (metadata != null)
                (description, supportedFormats) = ReadMetadata(metadata);

            var icon = archive.Entries.FirstOrDefault(entry => entry.FullName.Equals("pack.png", StringComparison.OrdinalIgnoreCase)) ??
                archive.Entries.FirstOrDefault(entry => entry.FullName.Equals("logo.png", StringComparison.OrdinalIgnoreCase)) ??
                archive.Entries.FirstOrDefault(entry => entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
            if (icon != null)
            {
                using var stream = icon.Open();
                using var data = new MemoryStream();
                stream.CopyTo(data);
                iconData = data.ToArray();
            }
        }
        catch (InvalidDataException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return new ResourcePackInfo(path, fileName, displayName, description, supportedFormats, file.Length,
            file.LastWriteTime, iconData, false, null, null, [], [], [], [], []);
    }

    private static ResourcePackInfo ReadBedrockPack(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = new DirectoryInfo(path);
        var fileName = directory.Name;
        var displayName = fileName;
        string? description = null;
        string? version = null;
        string? minEngineVersion = null;
        string? uuid = null;
        var authors = new List<string>();
        var subpacks = new List<string>();
        var capabilities = new List<string>();
        var modules = new List<string>();
        var dependencies = new List<string>();

        try
        {
            using var stream = File.OpenRead(Path.Combine(path, "manifest.json"));
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = document.RootElement;
            if (root.TryGetProperty("header", out var header) && header.ValueKind == JsonValueKind.Object)
            {
                displayName = GetString(header, "name") ?? displayName;
                description = GetString(header, "description");
                version = GetVersion(header, "version");
                minEngineVersion = GetVersion(header, "min_engine_version");
                uuid = GetString(header, "uuid");
            }
            if (root.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object &&
                metadata.TryGetProperty("authors", out var authorValues) && authorValues.ValueKind == JsonValueKind.Array)
                authors.AddRange(authorValues.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                    .Select(value => value.GetString()).OfType<string>().Where(value => !string.IsNullOrWhiteSpace(value)));
            if (root.TryGetProperty("subpacks", out var subpackValues) && subpackValues.ValueKind == JsonValueKind.Array)
                subpacks.AddRange(subpackValues.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.Object)
                    .Select(value => GetString(value, "name") ?? GetString(value, "folder_name")).OfType<string>());
            if (root.TryGetProperty("capabilities", out var capabilityValues) && capabilityValues.ValueKind == JsonValueKind.Array)
                capabilities.AddRange(capabilityValues.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                    .Select(value => value.GetString()).OfType<string>().Where(value => !string.IsNullOrWhiteSpace(value)));
            if (root.TryGetProperty("modules", out var moduleValues) && moduleValues.ValueKind == JsonValueKind.Array)
                modules.AddRange(moduleValues.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.Object)
                    .Select(value => GetString(value, "type")).OfType<string>());
            if (root.TryGetProperty("dependencies", out var dependencyValues) && dependencyValues.ValueKind == JsonValueKind.Array)
                dependencies.AddRange(dependencyValues.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.Object)
                    .Select(value => GetString(value, "module_name") ?? GetString(value, "uuid")).OfType<string>());
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }

        return new ResourcePackInfo(path, fileName, displayName, description, version, GetDirectorySize(directory, cancellationToken),
            directory.LastWriteTime, ReadIcon(Path.Combine(path, "pack_icon.png")), true, minEngineVersion, uuid, authors, subpacks, capabilities, modules, dependencies);
    }

    private static string? GetString(JsonElement element, string property) => element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;

    private static string? GetVersion(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString()?.Trim();
        if (value.ValueKind != JsonValueKind.Array)
            return null;
        var version = string.Join('.', value.EnumerateArray().Where(part => part.ValueKind == JsonValueKind.Number).Select(part => part.GetRawText()));
        return string.IsNullOrEmpty(version) ? null : version;
    }

    private static byte[]? ReadIcon(string path)
    {
        try { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static long GetDirectorySize(DirectoryInfo directory, CancellationToken cancellationToken)
    {
        try
        {
            return directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return file.Length;
            });
        }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static (string? Description, string? SupportedFormats) ReadMetadata(ZipArchiveEntry entry)
    {
        try
        {
            using var document = JsonDocument.Parse(entry.Open());
            if (!document.RootElement.TryGetProperty("pack", out var pack) || pack.ValueKind != JsonValueKind.Object)
                return (null, null);

            var description = pack.TryGetProperty("description", out var descriptionElement)
                ? GetText(descriptionElement)
                : null;
            var formats = pack.TryGetProperty("supported_formats", out var supported)
                ? GetFormats(supported)
                : pack.TryGetProperty("pack_format", out var format) ? GetFormats(format) : null;
            return (description, formats);
        }
        catch (JsonException) { return (null, null); }
    }

    private static string? GetFormats(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number when element.TryGetInt32(out var format) => format.ToString(),
        JsonValueKind.Array when element.GetArrayLength() == 2 && element[0].TryGetInt32(out var minimum) &&
            element[1].TryGetInt32(out var maximum) => $"[{minimum},{maximum}]",
        _ => null
    };

    private static string? GetText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString()?.Trim();

        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var parts = new List<string>();
        CollectText(element, parts);
        return string.Join(string.Empty, parts).Trim();
    }

    private static void CollectText(JsonElement element, List<string> parts)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            parts.Add(element.GetString() ?? string.Empty);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (element.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            parts.Add(text.GetString() ?? string.Empty);
        if (element.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Array)
            foreach (var child in extra.EnumerateArray())
                CollectText(child, parts);
    }
}

public sealed record ResourcePackInfo(
    string FilePath,
    string FileName,
    string DisplayName,
    string? Description,
    string? SupportedFormats,
    long FileSize,
    DateTime LastWriteTime,
    byte[]? IconData,
    bool IsBedrock,
    string? MinEngineVersion,
    string? Uuid,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> Subpacks,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Modules,
    IReadOnlyList<string> Dependencies);

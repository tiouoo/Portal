using System.IO.Compression;
using System.Text.Json;
using Portal.Core.Minecraft.Classes;

namespace Portal.Core.Minecraft.Services;

public sealed class ResourcePackService
{
    public Task<IReadOnlyList<ResourcePackInfo>> ScanAsync(MinecraftInstance instance,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(instance, cancellationToken), cancellationToken);

    private static IReadOnlyList<ResourcePackInfo> Scan(MinecraftInstance instance, CancellationToken cancellationToken)
    {
        if (instance.Type != MinecraftInstanceType.Java)
            return [];

        try
        {
            return Directory.EnumerateFiles(instance.GetSpecialFolder(MinecraftSpecialFolder.ResourcePacksFolder), "*.zip")
                .Select(path => ReadPack(path, cancellationToken))
                .OrderBy(pack => pack.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pack => pack.FileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static ResourcePackInfo ReadPack(string path, CancellationToken cancellationToken)
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
            file.LastWriteTime, iconData);
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
    byte[]? IconData);

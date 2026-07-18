using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Portal.Core.Minecraft.Classes;

namespace Portal.Core.Minecraft.Services;

public sealed class ModService
{
    public Task<IReadOnlyList<ModInfo>> ScanAsync(MinecraftInstance instance, CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(instance, cancellationToken), cancellationToken);

    private static IReadOnlyList<ModInfo> Scan(MinecraftInstance instance, CancellationToken cancellationToken)
    {
        if (instance.Type != MinecraftInstanceType.Java)
            return [];

        var modsPath = instance.GetSpecialFolder(MinecraftSpecialFolder.ModsFolder);
        if (!Directory.Exists(modsPath))
            return [];

        try
        {
            return Directory.EnumerateFiles(modsPath, "*.*", SearchOption.AllDirectories)
                .Where(IsModFile)
                .Select(path => ReadMod(path, cancellationToken))
                .OrderBy(mod => mod.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(mod => mod.FileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static bool IsModFile(string path) => Path.GetExtension(path).Equals(".jar", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".disabled", StringComparison.OrdinalIgnoreCase);

    private static ModInfo ReadMod(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = new FileInfo(path);
        var fileName = GetFileName(path);
        var (name, description) = ReadMetadata(path);
        return new ModInfo(path, fileName, name ?? fileName, description, Path.GetExtension(path)
            .Equals(".disabled", StringComparison.OrdinalIgnoreCase), file.Length, file.LastWriteTime);
    }

    private static string GetFileName(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase) ? name[..^13] : Path.GetFileNameWithoutExtension(name);
    }

    private static (string? Name, string? Description) ReadMetadata(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            return ReadTomlMetadata(archive, "META-INF/mods.toml") ?? ReadFabricMetadata(archive) ??
                   ReadMcmodMetadata(archive) ?? ReadTomlMetadata(archive, "META-INF/neoforge.mods.toml") ?? (null, null);
        }
        catch (InvalidDataException) { return (null, null); }
        catch (IOException) { return (null, null); }
        catch (UnauthorizedAccessException) { return (null, null); }
    }

    private static (string? Name, string? Description)? ReadTomlMetadata(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        if (entry == null) return null;
        try
        {
            using var reader = new StreamReader(entry.Open());
            var text = reader.ReadToEnd();
            var firstMod = Regex.Match(text, @"(?ms)^\s*\[\[mods\]\](?<content>.*?)(?=^\s*\[\[|\z)");
            if (!firstMod.Success) return null;
            var name = GetTomlString(firstMod.Groups["content"].Value, "displayName");
            var description = GetTomlString(firstMod.Groups["content"].Value, "description");
            if (name != null || description != null) return (name, description);
        }
        catch (Exception) { }
        return null;
    }

    private static (string? Name, string? Description)? ReadFabricMetadata(ZipArchive archive)
    {
        var entry = archive.GetEntry("fabric.mod.json");
        if (entry == null) return null;
        try
        {
            using var document = JsonDocument.Parse(entry.Open());
            var name = GetJsonString(document.RootElement, "name");
            var description = GetJsonString(document.RootElement, "description");
            return name != null || description != null ? (name, description) : null;
        }
        catch (JsonException) { return null; }
    }

    private static (string? Name, string? Description)? ReadMcmodMetadata(ZipArchive archive)
    {
        var entry = archive.GetEntry("mcmod.info");
        if (entry == null) return null;
        try
        {
            using var document = JsonDocument.Parse(entry.Open());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return null;
            var name = GetJsonString(root[0], "name");
            var description = GetJsonString(root[0], "description");
            return name != null || description != null ? (name, description) : null;
        }
        catch (JsonException) { return null; }
    }

    private static string? GetTomlString(string content, string key)
    {
        var match = Regex.Match(content, $"(?m)^\\s*{Regex.Escape(key)}\\s*=\\s*\\\"(?<value>(?:\\\\.|[^\\\"])*)\\\"");
        return match.Success && !string.IsNullOrWhiteSpace(match.Groups["value"].Value) ? match.Groups["value"].Value.Trim() : null;
    }

    private static string? GetJsonString(JsonElement element, string propertyName) => element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString()) ? property.GetString()!.Trim() : null;
}

public sealed record ModInfo(string FilePath, string FileName, string DisplayName, string? Description,
    bool IsDisabled, long FileSize, DateTime LastWriteTime);

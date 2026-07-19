using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Flurl.Http;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Services;

public sealed class ModService
{
    private const string CurseForgeFingerprintEndpoint = "https://api.curseforge.com/v1/fingerprints";
    private const string CurseForgeModsEndpoint = "https://api.curseforge.com/v1/mods";
    private const int FingerprintBatchSize = 50;
    private const int MaximumConcurrentRequests = 4;

    public async Task<IReadOnlyList<ModInfo>> ScanAsync(MinecraftInstance instance,
        CancellationToken cancellationToken = default)
    {
        var paths = await Task.Run(() => FindModFiles(instance), cancellationToken);
        var candidates = await Task.WhenAll(paths.Select(async path =>
        {
            try
            {
                return (Path: path,
                    Fingerprint: await Task.Run(() => CalculateCurseForgeFingerprint(path, cancellationToken), cancellationToken));
            }
            catch (IOException)
            {
                return (Path: path, Fingerprint: (uint?)null);
            }
            catch (UnauthorizedAccessException)
            {
                return (Path: path, Fingerprint: (uint?)null);
            }
        }));

        var results = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            ModInfo mod;
            if (candidate.Fingerprint is { } fingerprint && ReadCache(fingerprint) is { MetadataFetched: not false } cached)
                mod = CreateModInfo(candidate.Path, cached);
            else
            {
                mod = ReadMod(candidate.Path, cancellationToken);
                if (candidate.Fingerprint is { } missingFingerprint)
                    WriteCache(missingFingerprint, CreateLocalCacheEntry(mod));
            }

            results[candidate.Path] = mod;
        }

        return results.Values
            .OrderBy(mod => mod.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mod => mod.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task RefreshMetadataAsync(IEnumerable<ModInfo> mods, Action<ModInfo> metadataUpdated,
        Action<bool>? loadingChanged = null, CancellationToken cancellationToken = default)
    {
        if (ServiceCredentials.CurseForgeApiKey is null)
            return;

        var fingerprintedMods = await Task.WhenAll(mods.Select(async mod =>
        {
            try
            {
                return (Mod: mod,
                    Fingerprint: await Task.Run(() => CalculateCurseForgeFingerprint(mod.FilePath, cancellationToken),
                        cancellationToken));
            }
            catch (IOException)
            {
                return (Mod: mod, Fingerprint: (uint?)null);
            }
            catch (UnauthorizedAccessException)
            {
                return (Mod: mod, Fingerprint: (uint?)null);
            }
        }));

        var pending = new List<(ModInfo Mod, uint Fingerprint)>();
        foreach (var item in fingerprintedMods)
        {
            if (item.Fingerprint is not { } fingerprint) continue;
            var cached = ReadCache(fingerprint);
            if (cached is { MetadataFetched: not false, CurseForgeSlug: not null })
            {
                metadataUpdated(ApplyMetadata(item.Mod, cached));
                continue;
            }

            pending.Add((item.Mod, fingerprint));
        }

        loadingChanged?.Invoke(pending.Count > 0);
        using var semaphore = new SemaphoreSlim(MaximumConcurrentRequests);
        try
        {
            await Task.WhenAll(pending.Chunk(FingerprintBatchSize).Select(async batch =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    await FetchBatchAsync(batch, metadataUpdated, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }
        finally
        {
            loadingChanged?.Invoke(false);
        }
    }

    private static IReadOnlyList<string> FindModFiles(MinecraftInstance instance)
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
                .ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsModFile(string path) =>
        Path.GetExtension(path).Equals(".jar", StringComparison.OrdinalIgnoreCase) ||
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

    private static ModInfo CreateModInfo(string path, ModCacheEntry entry)
    {
        var file = new FileInfo(path);
        var fileName = GetFileName(path);
        return new ModInfo(path, fileName, entry.DisplayName ?? fileName, entry.Description,
            Path.GetExtension(path).Equals(".disabled", StringComparison.OrdinalIgnoreCase), file.Length,
            file.LastWriteTime, entry.IconUrl, entry.FriendlyName);
    }

    private static ModCacheEntry CreateLocalCacheEntry(ModInfo mod) => new()
    {
        DisplayName = mod.DisplayName,
        Description = mod.Description,
        IconUrl = mod.IconUrl,
        FriendlyName = mod.FriendlyName,
        MetadataFetched = false
    };

    private static async Task FetchBatchAsync((ModInfo Mod, uint Fingerprint)[] batch, Action<ModInfo> metadataUpdated,
        CancellationToken cancellationToken)
    {
        var entries = await FetchMetadataBatchAsync(batch.Select(item => item.Fingerprint).ToArray(), cancellationToken);

        foreach (var item in batch)
        {
            entries.TryGetValue(item.Fingerprint, out var entry);
            var cached = (entry ?? CreateLocalCacheEntry(item.Mod)) with
            {
                FriendlyName = null,
                IsWikiFriendlyName = false
            };

            WriteCache(item.Fingerprint, cached);
            metadataUpdated(ApplyMetadata(item.Mod, cached));
        }
    }

    private static async Task<Dictionary<uint, ModCacheEntry?>> FetchMetadataBatchAsync(IEnumerable<uint> fingerprints,
        CancellationToken cancellationToken)
    {
        var requested = fingerprints.Distinct().ToArray();
        var response = await CurseForgeFingerprintEndpoint
            .WithHeader("Accept", "application/json")
            .WithHeader("x-api-key", ServiceCredentials.CurseForgeApiKey!)
            .PostJsonAsync(new { fingerprints = requested }, cancellationToken: cancellationToken)
            .ReceiveJson<CurseForgeFingerprintResponse>();
        var matches = response.Data?.ExactMatches
            ?.Where(match => match.File != null)
            .ToDictionary(match => match.File!.Fingerprint) ?? [];
        var entries = new Dictionary<uint, ModCacheEntry?>();
        foreach (var fingerprint in requested)
        {
            matches.TryGetValue(fingerprint, out var match);
            if (match?.File == null)
            {
                entries[fingerprint] = null;
                continue;
            }

            var entry = new ModCacheEntry
            {
                DisplayName = match.File.DisplayName,
                ProjectId = match.File.ModId,
                FileId = match.File.Id,
                MetadataFetched = true
            };
            try
            {
                entry = await GetMetadataAsync(match.File, cancellationToken);
            }
            catch (FlurlHttpException)
            {
            }

            entries[fingerprint] = entry;
        }

        return entries;
    }

    public async Task CacheFriendlyNamesAsync(IEnumerable<ModInfo> mods, Func<string, string?> findFriendlyName,
        Action<ModInfo> friendlyNameUpdated, CancellationToken cancellationToken = default)
    {
        foreach (var mod in mods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var fingerprint = await Task.Run(() => CalculateCurseForgeFingerprint(mod.FilePath, cancellationToken),
                    cancellationToken);
                var cached = ReadCache(fingerprint);
                if (cached is { IsWikiFriendlyName: true, FriendlyName: not null })
                {
                    friendlyNameUpdated(ApplyMetadata(mod, cached));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(cached?.CurseForgeSlug))
                {
                    if (cached != null)
                        friendlyNameUpdated(ApplyMetadata(mod, cached));
                    continue;
                }

                var friendlyName = findFriendlyName(cached.CurseForgeSlug);
                if (string.Equals(cached.FriendlyName, friendlyName, StringComparison.Ordinal))
                    continue;

                cached = cached with { FriendlyName = friendlyName, IsWikiFriendlyName = !string.IsNullOrWhiteSpace(friendlyName) };
                WriteCache(fingerprint, cached);
                friendlyNameUpdated(ApplyMetadata(mod, cached));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<ModCacheEntry> GetMetadataAsync(CurseForgeFile file, CancellationToken cancellationToken)
    {
        var mod = await $"{CurseForgeModsEndpoint}/{file.ModId}"
            .WithHeader("Accept", "application/json")
            .WithHeader("x-api-key", ServiceCredentials.CurseForgeApiKey!)
            .GetJsonAsync<CurseForgeModResponse>(cancellationToken: cancellationToken);
        return new ModCacheEntry
        {
            DisplayName = mod.Data?.Name ?? file.DisplayName,
            Description = mod.Data?.Summary,
            IconUrl = mod.Data?.Logo?.ThumbnailUrl ?? mod.Data?.Logo?.Url,
            CurseForgeSlug = mod.Data?.Slug,
            ProjectId = file.ModId,
            FileId = file.Id,
            MetadataFetched = true
        };
    }

    private static ModInfo ApplyMetadata(ModInfo mod, ModCacheEntry entry) => mod with
    {
        DisplayName = entry.DisplayName ?? mod.DisplayName,
        Description = entry.Description ?? mod.Description,
        IconUrl = entry.IconUrl ?? mod.IconUrl,
        FriendlyName = entry.FriendlyName ?? mod.FriendlyName
    };

    private static uint CalculateCurseForgeFingerprint(string path, CancellationToken cancellationToken)
    {
        using var source = File.OpenRead(path);
        using var filtered = new MemoryStream();
        while (source.ReadByte() is var value and >= 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurseForgeWhitespace((byte)value))
                filtered.WriteByte((byte)value);
        }

        var bytes = filtered.GetBuffer().AsSpan(0, checked((int)filtered.Length));
        uint hash = 1 ^ (uint)bytes.Length;
        var offset = 0;
        while (bytes.Length - offset >= 4)
        {
            Mix(ref hash, BitConverter.ToUInt32(bytes[offset..]));
            offset += 4;
        }

        switch (bytes.Length - offset)
        {
            case 3:
                hash ^= (uint)bytes[offset + 2] << 16;
                goto case 2;
            case 2:
                hash ^= (uint)bytes[offset + 1] << 8;
                goto case 1;
            case 1:
                hash ^= bytes[offset];
                hash *= 0x5bd1e995u;
                break;
        }

        hash ^= hash >> 13;
        hash *= 0x5bd1e995u;
        hash ^= hash >> 15;
        return hash;
    }

    private static bool IsCurseForgeWhitespace(byte value) => value is 0x20 or 0x09 or 0x0a or 0x0d;

    private static void Mix(ref uint hash, uint value)
    {
        value *= 0x5bd1e995u;
        value ^= value >> 24;
        value *= 0x5bd1e995u;
        hash *= 0x5bd1e995u;
        hash ^= value;
    }

    private static ModCacheEntry? ReadCache(uint fingerprint) => CacheDatabase.ReadMod(fingerprint);

    private static void WriteCache(uint fingerprint, ModCacheEntry entry) => CacheDatabase.WriteMod(fingerprint, entry);

    private static string GetFileName(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)
            ? name[..^13]
            : Path.GetFileNameWithoutExtension(name);
    }

    private static (string? Name, string? Description) ReadMetadata(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            return ReadTomlMetadata(archive, "META-INF/mods.toml") ?? ReadFabricMetadata(archive) ??
                ReadMcmodMetadata(archive) ?? ReadTomlMetadata(archive, "META-INF/neoforge.mods.toml") ?? (null, null);
        }
        catch (InvalidDataException)
        {
            return (null, null);
        }
        catch (IOException)
        {
            return (null, null);
        }
        catch (UnauthorizedAccessException)
        {
            return (null, null);
        }
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
        catch (Exception)
        {
        }

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
        catch (JsonException)
        {
            return null;
        }
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
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetTomlString(string content, string key)
    {
        var match = Regex.Match(content, $"(?m)^\\s*{Regex.Escape(key)}\\s*=\\s*\\\"(?<value>(?:\\\\.|[^\\\"])*)\\\"");
        return match.Success && !string.IsNullOrWhiteSpace(match.Groups["value"].Value)
            ? match.Groups["value"].Value.Trim()
            : null;
    }

    private static string? GetJsonString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!.Trim()
            : null;
}

public sealed record ModInfo(
    string FilePath,
    string FileName,
    string DisplayName,
    string? Description,
    bool IsDisabled,
    long FileSize,
    DateTime LastWriteTime,
    string? IconUrl = null,
    string? FriendlyName = null);

internal sealed record ModCacheEntry
{
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string? IconUrl { get; init; }
    public int? ProjectId { get; init; }
    public int? FileId { get; init; }
    public string? FriendlyName { get; init; }
    public bool? MetadataFetched { get; init; }
    public string? CurseForgeSlug { get; init; }
    public bool IsWikiFriendlyName { get; init; }
}

internal sealed class CurseForgeFingerprintResponse
{
    [JsonPropertyName("data")] public CurseForgeFingerprintData? Data { get; init; }
}

internal sealed class CurseForgeFingerprintData
{
    [JsonPropertyName("exactMatches")] public List<CurseForgeMatch>? ExactMatches { get; init; }
}

internal sealed class CurseForgeMatch
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("file")] public CurseForgeFile? File { get; init; }
}

internal sealed class CurseForgeFile
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("modId")] public int ModId { get; init; }
    [JsonPropertyName("fileFingerprint")] public uint Fingerprint { get; init; }
    [JsonPropertyName("displayName")] public string? DisplayName { get; init; }
}

internal sealed class CurseForgeModResponse
{
    [JsonPropertyName("data")] public CurseForgeMod? Data { get; init; }
}

internal sealed class CurseForgeMod
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("summary")] public string? Summary { get; init; }
    [JsonPropertyName("logo")] public CurseForgeLogo? Logo { get; init; }
    [JsonPropertyName("slug")] public string? Slug { get; init; }
}

internal sealed class CurseForgeLogo
{
    [JsonPropertyName("thumbnailUrl")] public string? ThumbnailUrl { get; init; }
    [JsonPropertyName("url")] public string? Url { get; init; }
}

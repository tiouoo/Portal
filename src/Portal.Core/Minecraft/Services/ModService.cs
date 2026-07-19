using System.IO.Compression;
using System.Security.Cryptography;
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
    private const string ModrinthVersionFilesEndpoint = "https://api.modrinth.com/v2/version_files";
    private const string ModrinthProjectsEndpoint = "https://api.modrinth.com/v2/projects";
    private const string ModrinthUserAgent = "Portal/1.0 (https://github.com/tiouoo/Portal)";
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
                    Sha1: await Task.Run(() => CalculateSha1(path, cancellationToken), cancellationToken),
                    Fingerprint: await Task.Run(() => CalculateCurseForgeFingerprint(path, cancellationToken), cancellationToken));
            }
            catch (IOException)
            {
                return (Path: path, Sha1: (string?)null, Fingerprint: (uint?)null);
            }
            catch (UnauthorizedAccessException)
            {
                return (Path: path, Sha1: (string?)null, Fingerprint: (uint?)null);
            }
        }));

        var results = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            ModInfo mod;
            if (candidate.Sha1 is { } sha1 && ReadCache(sha1) is { MetadataFetched: not false } cached)
                mod = CreateModInfo(candidate.Path, cached);
            else if (candidate.Fingerprint is { } fingerprint && ReadCache(fingerprint) is { MetadataFetched: not false } fingerprintCached)
                mod = CreateModInfo(candidate.Path, fingerprintCached);
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

    public async Task RefreshMetadataAsync(IEnumerable<ModInfo> mods, Func<string, string?>? findFriendlyName,
        Action<ModInfo> metadataUpdated, Action<bool>? loadingChanged = null, CancellationToken cancellationToken = default)
    {
        var fingerprintedMods = await Task.WhenAll(mods.Select(async mod =>
        {
            try
            {
                return (Mod: mod,
                    Sha1: await Task.Run(() => CalculateSha1(mod.FilePath, cancellationToken), cancellationToken),
                    Fingerprint: await Task.Run(() => CalculateCurseForgeFingerprint(mod.FilePath, cancellationToken),
                        cancellationToken));
            }
            catch (IOException)
            {
                return (Mod: mod, Sha1: (string?)null, Fingerprint: (uint?)null);
            }
            catch (UnauthorizedAccessException)
            {
                return (Mod: mod, Sha1: (string?)null, Fingerprint: (uint?)null);
            }
        }));

        var pending = new List<(ModInfo Mod, string Sha1, uint? Fingerprint)>();
        foreach (var item in fingerprintedMods)
        {
            if (item.Sha1 is not { } sha1) continue;
            var cached = ReadCache(sha1);
            if (cached is { MetadataFetched: not false })
            {
                metadataUpdated(ApplyMetadata(item.Mod, cached));
                continue;
            }

            pending.Add((item.Mod, sha1, item.Fingerprint));
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
                    await FetchBatchAsync(batch, findFriendlyName, metadataUpdated, cancellationToken);
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
            file.LastWriteTime, entry.IconUrl, entry.FriendlyName, entry.Source,
            entry.Source == "Modrinth" ? entry.ModrinthProjectId : entry.ProjectId?.ToString(),
            entry.Source == "Modrinth" ? entry.ModrinthVersionId : entry.FileId?.ToString());
    }

    private static ModCacheEntry CreateLocalCacheEntry(ModInfo mod) => new()
    {
        DisplayName = mod.DisplayName,
        Description = mod.Description,
        IconUrl = mod.IconUrl,
        FriendlyName = mod.FriendlyName,
        MetadataFetched = false
    };

    private static async Task FetchBatchAsync((ModInfo Mod, string Sha1, uint? Fingerprint)[] batch,
        Func<string, string?>? findFriendlyName, Action<ModInfo> metadataUpdated, CancellationToken cancellationToken)
    {
        var entries = await FetchModrinthMetadataBatchAsync(batch.Select(item => item.Sha1), cancellationToken);
        var missing = batch.Where(item => !entries.ContainsKey(item.Sha1) && item.Fingerprint.HasValue)
            .Select(item => item.Fingerprint!.Value).ToArray();
        var curseForgeEntries = missing.Length == 0 || ServiceCredentials.CurseForgeApiKey is null
            ? [] : await FetchMetadataBatchAsync(missing, cancellationToken);

        foreach (var item in batch)
        {
            entries.TryGetValue(item.Sha1, out var entry);
            if (entry == null && item.Fingerprint is { } fingerprint)
                curseForgeEntries.TryGetValue(fingerprint, out entry);
            var cached = (entry ?? CreateLocalCacheEntry(item.Mod)) with
            {
                FriendlyName = null,
                IsWikiFriendlyName = false
            };
            if (GetFriendlyNameSlug(cached) is { } slug && findFriendlyName?.Invoke(slug) is { } friendlyName)
                cached = cached with { FriendlyName = friendlyName, IsWikiFriendlyName = true };

            WriteCache(item.Sha1, cached);
            if (item.Fingerprint is { } cacheFingerprint)
                WriteCache(cacheFingerprint, cached);
            metadataUpdated(ApplyMetadata(item.Mod, cached));
        }
    }

    private static async Task<Dictionary<string, ModCacheEntry>> FetchModrinthMetadataBatchAsync(IEnumerable<string> hashes,
        CancellationToken cancellationToken)
    {
        var requested = hashes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (requested.Length == 0) return [];

        Dictionary<string, ModrinthVersion> response;
        try
        {
            response = await ModrinthVersionFilesEndpoint
                .WithHeader("Accept", "application/json")
                .WithHeader("User-Agent", ModrinthUserAgent)
                .PostJsonAsync(new { hashes = requested, algorithm = "sha1" }, cancellationToken: cancellationToken)
                .ReceiveJson<Dictionary<string, ModrinthVersion>>();
        }
        catch (FlurlHttpException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
        var projects = await FetchModrinthProjectsAsync(response.Values.Select(version => version.ProjectId), cancellationToken);
        return response.ToDictionary(pair => pair.Key, pair =>
        {
            projects.TryGetValue(pair.Value.ProjectId ?? string.Empty, out var project);
            return new ModCacheEntry
            {
                DisplayName = project?.Title ?? pair.Value.Name ?? pair.Value.VersionNumber,
                Description = project?.Description,
                IconUrl = project?.IconUrl,
                MetadataFetched = true,
                Source = "Modrinth",
                ModrinthProjectId = pair.Value.ProjectId,
                ModrinthVersionId = pair.Value.Id,
                ModrinthSlug = project?.Slug
            };
        }, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<Dictionary<string, ModrinthProject>> FetchModrinthProjectsAsync(IEnumerable<string?> projectIds,
        CancellationToken cancellationToken)
    {
        var requested = projectIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (requested.Length == 0) return [];

        try
        {
            var projects = await ModrinthProjectsEndpoint
                .WithHeader("Accept", "application/json")
                .WithHeader("User-Agent", ModrinthUserAgent)
                .SetQueryParam("ids", JsonSerializer.Serialize(requested))
                .GetJsonAsync<List<ModrinthProject>>(cancellationToken: cancellationToken);
            return projects.Where(project => !string.IsNullOrWhiteSpace(project.Id))
                .ToDictionary(project => project.Id!, StringComparer.Ordinal);
        }
        catch (FlurlHttpException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
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
                MetadataFetched = true,
                Source = "CurseForge"
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
                if (cached == null || GetFriendlyNameSlug(cached) == null)
                {
                    if (cached != null)
                        friendlyNameUpdated(ApplyMetadata(mod, cached));
                    continue;
                }

                var friendlyName = findFriendlyName(GetFriendlyNameSlug(cached)!);
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
            MetadataFetched = true,
            Source = "CurseForge"
        };
    }

    private static ModInfo ApplyMetadata(ModInfo mod, ModCacheEntry entry) => mod with
    {
        DisplayName = entry.DisplayName ?? mod.DisplayName,
        Description = entry.Description ?? mod.Description,
        IconUrl = entry.IconUrl ?? mod.IconUrl,
        FriendlyName = entry.FriendlyName ?? mod.FriendlyName,
        Source = entry.Source ?? mod.Source,
        ProjectId = entry.Source == "Modrinth" ? entry.ModrinthProjectId : entry.ProjectId?.ToString(),
        VersionId = entry.Source == "Modrinth" ? entry.ModrinthVersionId : entry.FileId?.ToString()
    };

    private static string? GetFriendlyNameSlug(ModCacheEntry entry) => entry.Source == "Modrinth"
        ? entry.ModrinthSlug
        : entry.CurseForgeSlug;

    private static string CalculateSha1(string path, CancellationToken cancellationToken)
    {
        using var source = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = new byte[81920];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

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

    private static ModCacheEntry? ReadCache(string sha1) => CacheDatabase.ReadMod(sha1);

    private static void WriteCache(uint fingerprint, ModCacheEntry entry) => CacheDatabase.WriteMod(fingerprint, entry);

    private static void WriteCache(string sha1, ModCacheEntry entry) => CacheDatabase.WriteMod(sha1, entry);

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
    string? FriendlyName = null,
    string? Source = null,
    string? ProjectId = null,
    string? VersionId = null);

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
    public string? Source { get; init; }
    public string? ModrinthProjectId { get; init; }
    public string? ModrinthVersionId { get; init; }
    public string? ModrinthSlug { get; init; }
    public bool IsWikiFriendlyName { get; init; }
}

internal sealed class ModrinthVersion
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("project_id")] public string? ProjectId { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("version_number")] public string? VersionNumber { get; init; }
}

internal sealed class ModrinthProject
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("icon_url")] public string? IconUrl { get; init; }
    [JsonPropertyName("slug")] public string? Slug { get; init; }
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

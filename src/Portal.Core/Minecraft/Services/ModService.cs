using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Iridium.Enums;
using Iridium.Helpers.Resources;
using Iridium.Resources.CurseForge;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Services;

public sealed class ModService
{
    private const int FingerprintBatchSize = 50;
    private const int MaximumConcurrentRequests = 4;
    private const int MaximumConcurrentHashes = 4;

    public async Task<IReadOnlyList<ModInfo>> ScanAsync(MinecraftInstance instance,
        CancellationToken cancellationToken = default)
    {
        var paths = await Task.Run(() => FindModFiles(instance), cancellationToken);
        var candidates = await ComputeHashesAsync(paths, cancellationToken);

        return await Task.Run(() => BuildScanResults(candidates, cancellationToken), cancellationToken);
    }

    private static IReadOnlyList<ModInfo> BuildScanResults(
        IEnumerable<(string Path, string? Sha1, uint? Fingerprint)> candidates, CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                ModInfo mod;
                if (candidate.Sha1 is { } sha1 && ReadCache(sha1) is { MetadataFetched: not false } cached)
                {
                    mod = CreateModInfo(candidate.Path, cached);
                }
                else if (candidate.Fingerprint is { } fingerprint && ReadCache(fingerprint) is
                             { MetadataFetched: not false } fingerprintCached)
                {
                    mod = CreateModInfo(candidate.Path, fingerprintCached);
                }
                else
                {
                    mod = ReadMod(candidate.Path, cancellationToken);
                    if (candidate.Fingerprint is { } missingFingerprint)
                        WriteCache(missingFingerprint, CreateLocalCacheEntry(mod));
                }


                results[candidate.Path] = mod with { Sha1 = candidate.Sha1, Fingerprint = candidate.Fingerprint };
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return results.Values
            .OrderBy(mod => mod.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mod => mod.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task RefreshMetadataAsync(IEnumerable<ModInfo> mods, Func<string, string?>? findFriendlyName,
        Action<ModInfo> metadataUpdated, Action<bool>? loadingChanged = null,
        CancellationToken cancellationToken = default)
    {
        using var hashSemaphore = new SemaphoreSlim(MaximumConcurrentHashes);
        var fingerprintedMods = await Task.WhenAll(
            mods.Select<ModInfo, Task<(ModInfo Mod, string? Sha1, uint? Fingerprint)>>(async mod =>
            {
                if (mod.Sha1 != null)
                    return (Mod: mod, mod.Sha1, mod.Fingerprint);

                await hashSemaphore.WaitAsync(cancellationToken);
                try
                {
                    var hashes = await Task.Run(() => ComputeHashes(mod.FilePath, cancellationToken), cancellationToken);
                    return (Mod: mod, Sha1: (string?)hashes.Sha1, hashes.Fingerprint);
                }
                catch (IOException)
                {
                    return (Mod: mod, Sha1: null, Fingerprint: null);
                }
                catch (UnauthorizedAccessException)
                {
                    return (Mod: mod, Sha1: null, Fingerprint: null);
                }
                finally
                {
                    hashSemaphore.Release();
                }
            }));

        var pending = new List<(ModInfo Mod, string Sha1, uint? Fingerprint)>();
        var translationPending = new List<(ModInfo Mod, string Sha1, uint? Fingerprint, ModCacheEntry Entry)>();
        foreach (var item in fingerprintedMods)
        {
            if (item.Sha1 is not { } sha1) continue;
            var cached = ReadCache(sha1);
            if (cached is { MetadataFetched: not false })
            {
                metadataUpdated(ApplyMetadata(item.Mod, cached));
                if (cached.TranslatedDescription == null && GetTranslationIdentity(cached) != null)
                    translationPending.Add((item.Mod, sha1, item.Fingerprint, cached));
                continue;
            }

            pending.Add((item.Mod, sha1, item.Fingerprint));
        }

        loadingChanged?.Invoke(pending.Count > 0 || translationPending.Count > 0);
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
            await TranslateCachedBatchAsync(translationPending.ToArray(), metadataUpdated, cancellationToken);
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

    private static bool IsModFile(string path)
    {
        return Path.GetExtension(path).Equals(".jar", StringComparison.OrdinalIgnoreCase) ||
               Path.GetExtension(path).Equals(".disabled", StringComparison.OrdinalIgnoreCase);
    }

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
        return new ModInfo(path, fileName, entry.DisplayName ?? fileName,
            entry.TranslatedDescription ?? entry.Description,
            Path.GetExtension(path).Equals(".disabled", StringComparison.OrdinalIgnoreCase), file.Length,
            file.LastWriteTime, entry.IconUrl, entry.FriendlyName, entry.Source,
            entry.Source == "Modrinth" ? entry.ModrinthProjectId : entry.ProjectId?.ToString(),
            entry.Source == "Modrinth" ? entry.ModrinthVersionId : entry.FileId?.ToString());
    }

    private static ModCacheEntry CreateLocalCacheEntry(ModInfo mod)
    {
        return new ModCacheEntry
        {
            DisplayName = mod.DisplayName,
            Description = mod.Description,
            IconUrl = mod.IconUrl,
            FriendlyName = mod.FriendlyName,
            MetadataFetched = false
        };
    }

    private static async Task FetchBatchAsync((ModInfo Mod, string Sha1, uint? Fingerprint)[] batch,
        Func<string, string?>? findFriendlyName, Action<ModInfo> metadataUpdated, CancellationToken cancellationToken)
    {
        var fingerprinted = batch.Where(item => item.Fingerprint.HasValue)
            .Select(item => item.Fingerprint!.Value).ToArray();
        var curseForgeEntries = fingerprinted.Length == 0 || CredentialsService.CurseForgeApiKey is null
            ? new Dictionary<uint, ModCacheEntry?>()
            : await FetchMetadataBatchAsync(fingerprinted, cancellationToken);

        var missingSha1 = batch.Where(item =>
                !(item.Fingerprint is { } fingerprint &&
                  curseForgeEntries.TryGetValue(fingerprint, out var matched) && matched is not null))
            .Select(item => item.Sha1).ToArray();
        var modrinthEntries = missingSha1.Length == 0
            ? new Dictionary<string, ModCacheEntry>(StringComparer.OrdinalIgnoreCase)
            : await FetchModrinthMetadataBatchAsync(missingSha1, cancellationToken);

        await TranslateEntriesAsync(curseForgeEntries.Values.Concat(modrinthEntries.Values).OfType<ModCacheEntry>(),
            cancellationToken);

        foreach (var item in batch)
        {
            ModCacheEntry? entry = null;
            if (item.Fingerprint is { } fingerprint &&
                curseForgeEntries.TryGetValue(fingerprint, out var curseForgeEntry) &&
                curseForgeEntry is not null)
                entry = curseForgeEntry;
            if (entry == null)
                modrinthEntries.TryGetValue(item.Sha1, out entry);
            var cached = (entry ?? CreateLocalCacheEntry(item.Mod)) with
            {
                FriendlyName = null,
                IsWikiFriendlyName = false
            };
            if (GetFriendlyNameSlug(cached) is { } slug && findFriendlyName?.Invoke(slug) is { } friendlyName)
                cached = cached with { FriendlyName = friendlyName, IsWikiFriendlyName = true };

            if (item.Fingerprint is { } cacheFingerprint)
                WriteCache(cacheFingerprint, item.Sha1, cached);
            else
                WriteCache(item.Sha1, cached);
            metadataUpdated(ApplyMetadata(item.Mod, cached));
        }
    }

    private static async Task TranslateEntriesAsync(IEnumerable<ModCacheEntry> entries,
        CancellationToken cancellationToken)
    {
        var grouped = entries.Where(entry => entry.TranslatedDescription == null)
            .Select(entry => (Entry: entry, Identity: GetTranslationIdentity(entry)))
            .Where(item => item.Identity != null).GroupBy(item => item.Identity!.Value.Source);
        foreach (var group in grouped)
        {
            var translations = await ProjectTranslationService.GetTranslationsAsync(group.Key,
                group.Select(item => item.Identity!.Value.ProjectId), cancellationToken);
            foreach (var item in group)
                if (translations.TryGetValue(item.Identity!.Value.ProjectId, out var translated))
                    item.Entry.TranslatedDescription = translated;
        }
    }

    private static async Task TranslateCachedBatchAsync(
        (ModInfo Mod, string Sha1, uint? Fingerprint, ModCacheEntry Entry)[] batch,
        Action<ModInfo> metadataUpdated, CancellationToken cancellationToken)
    {
        await TranslateEntriesAsync(batch.Select(item => item.Entry), cancellationToken);
        foreach (var item in batch.Where(item => item.Entry.TranslatedDescription != null))
        {
            if (item.Fingerprint is { } fingerprint) WriteCache(fingerprint, item.Sha1, item.Entry);
            else WriteCache(item.Sha1, item.Entry);
            metadataUpdated(ApplyMetadata(item.Mod, item.Entry));
        }
    }

    private static (ProjectTranslationSource Source, string ProjectId)? GetTranslationIdentity(ModCacheEntry entry)
    {
        return entry.Source switch
        {
            "Modrinth" when !string.IsNullOrWhiteSpace(entry.ModrinthProjectId) =>
                (ProjectTranslationSource.Modrinth, entry.ModrinthProjectId),
            "CurseForge" when entry.ProjectId.HasValue =>
                (ProjectTranslationSource.CurseForge, entry.ProjectId.Value.ToString()),
            _ => null
        };
    }

    private static async Task<Dictionary<string, ModCacheEntry>> FetchModrinthMetadataBatchAsync(
        IEnumerable<string> hashes,
        CancellationToken cancellationToken)
    {
        var requested = hashes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (requested.Length == 0) return [];

        IReadOnlyDictionary<string, Iridium.Models.Resources.ResourceFile?> response;
        try
        {
            response = await IridiumResourceClients.Modrinth.GetFilesByHashesAsync(requested,
                Iridium.Enums.HashAlgorithm.Sha1, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Warning($"[ModService] Modrinth metadata lookup failed: {exception}");
            return [];
        }

        var versions = response.Values.OfType<Iridium.Models.Resources.ResourceFile>().ToArray();
        var projects = await FetchModrinthProjectsAsync(
            versions.Select(version => version.ProjectId), cancellationToken);
        return response
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair =>
            {
                var version = pair.Value!;
                projects.TryGetValue(version.ProjectId ?? string.Empty, out var project);
                return new ModCacheEntry
                {
                    DisplayName = project?.Title ?? version.Name ?? version.VersionNumber,
                    Description = project?.Description,
                    IconUrl = project?.IconUrl,
                    MetadataFetched = true,
                    Source = "Modrinth",
                    ModrinthProjectId = version.ProjectId,
                    ModrinthVersionId = version.Id,
                    ModrinthSlug = project?.Slug
                };
            }, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<Dictionary<string, Iridium.Models.Resources.ResourceProject>> FetchModrinthProjectsAsync(
        IEnumerable<string?> projectIds,
        CancellationToken cancellationToken)
    {
        var requested = projectIds.Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>()
            .Distinct(StringComparer.Ordinal).ToArray();
        if (requested.Length == 0) return [];

        try
        {
            var projects = await IridiumResourceClients.Modrinth.GetProjectsAsync(requested, cancellationToken);
            return projects.Where(project => !string.IsNullOrWhiteSpace(project.Id))
                .ToDictionary(project => project.Id, StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Warning($"[ModService] Modrinth project lookup failed: {exception}");
            return [];
        }
    }

    private static async Task<Dictionary<uint, ModCacheEntry?>> FetchMetadataBatchAsync(IEnumerable<uint> fingerprints,
        CancellationToken cancellationToken)
    {
        var requested = fingerprints.Distinct().ToArray();
        Iridium.Resources.CurseForge.CurseForgeFingerprintResult response;
        try
        {
            response = await IridiumResourceClients.CurseForge.GetFilesByFingerprintsAsync(requested,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Warning($"[ModService] CurseForge fingerprint lookup failed: {exception}");
            return [];
        }

        var matches = response.Data?.ExactMatches
            ?.Where(match => match.File is { FileFingerprint: not null })
            .ToDictionary(match => match.File!.FileFingerprint!.Value) ?? [];
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
                ProjectId = (int?)match.File.ModId,
                FileId = (int)match.File.Id,
                MetadataFetched = true,
                Source = "CurseForge"
            };
            try
            {
                entry = await GetMetadataAsync(match.File, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
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
                var fingerprint = mod.Fingerprint ??
                                  (await Task.Run(() => ComputeHashes(mod.FilePath, cancellationToken),
                                      cancellationToken)).Fingerprint;
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

                cached = cached with
                {
                    FriendlyName = friendlyName, IsWikiFriendlyName = !string.IsNullOrWhiteSpace(friendlyName)
                };
                WriteCache(fingerprint, cached);
                friendlyNameUpdated(ApplyMetadata(mod, cached));
            }
            catch (IOException exception)
            {
                Logger.Error(string.Format(LogLanguageManager.Instance.modService_updateFriendlyNameCacheFailed.CurrentValue(), mod.FilePath), exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                Logger.Error(string.Format(LogLanguageManager.Instance.modService_updateFriendlyNameCacheDenied.CurrentValue(), mod.FilePath), exception);
            }
        }
    }

    private static async Task<ModCacheEntry> GetMetadataAsync(Iridium.Resources.CurseForge.CurseForgeFile file,
        CancellationToken cancellationToken)
    {
        var mod = file.ModId is { } modId
            ? await IridiumResourceClients.CurseForge.GetProjectAsync(modId.ToString(), cancellationToken)
            : null;
        return new ModCacheEntry
        {
            DisplayName = mod?.Title ?? file.DisplayName,
            Description = mod?.Description,
            IconUrl = mod?.IconUrl,
            CurseForgeSlug = mod?.Slug,
            ProjectId = (int?)file.ModId,
            FileId = (int)file.Id,
            MetadataFetched = true,
            Source = "CurseForge"
        };
    }

    private static ModInfo ApplyMetadata(ModInfo mod, ModCacheEntry entry)
    {
        return mod with
        {
            DisplayName = entry.DisplayName ?? mod.DisplayName,
            Description = entry.TranslatedDescription ?? entry.Description ?? mod.Description,
            IconUrl = entry.IconUrl ?? mod.IconUrl,
            FriendlyName = entry.FriendlyName ?? mod.FriendlyName,
            Source = entry.Source ?? mod.Source,
            ProjectId = entry.Source == "Modrinth" ? entry.ModrinthProjectId : entry.ProjectId?.ToString(),
            VersionId = entry.Source == "Modrinth" ? entry.ModrinthVersionId : entry.FileId?.ToString()
        };
    }

    private static string? GetFriendlyNameSlug(ModCacheEntry entry)
    {
        return entry.Source == "Modrinth"
            ? entry.ModrinthSlug
            : entry.CurseForgeSlug;
    }

    private static async Task<(string Path, string? Sha1, uint? Fingerprint)[]> ComputeHashesAsync(
        IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(MaximumConcurrentHashes);
        return await Task.WhenAll(paths.Select(async path =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var hashes = await Task.Run(() => ComputeHashes(path, cancellationToken), cancellationToken);
                return (Path: path, Sha1: (string?)hashes.Sha1, Fingerprint: (uint?)hashes.Fingerprint);
            }
            catch (IOException)
            {
                return (Path: path, Sha1: null, Fingerprint: null);
            }
            catch (UnauthorizedAccessException)
            {
                return (Path: path, Sha1: null, Fingerprint: null);
            }
            finally
            {
                semaphore.Release();
            }
        }));
    }

    internal static (string Sha1, uint Fingerprint) ComputeHashes(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = File.ReadAllBytes(path);
        cancellationToken.ThrowIfCancellationRequested();
        return (Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant(),
            CurseForgeFingerprintHelper.Compute(bytes));
    }

    private static ModCacheEntry? ReadCache(uint fingerprint)
    {
        return CacheDatabase.ReadMod(fingerprint);
    }

    private static ModCacheEntry? ReadCache(string sha1)
    {
        return CacheDatabase.ReadMod(sha1);
    }

    private static void WriteCache(uint fingerprint, ModCacheEntry entry)
    {
        CacheDatabase.WriteMod(fingerprint, entry);
    }

    private static void WriteCache(string sha1, ModCacheEntry entry)
    {
        CacheDatabase.WriteMod(sha1, entry);
    }

    private static void WriteCache(uint fingerprint, string sha1, ModCacheEntry entry)
    {
        CacheDatabase.WriteMod(fingerprint, sha1, entry);
    }

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

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!.Trim()
            : null;
    }
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
    string? VersionId = null,
    string? Sha1 = null,
    uint? Fingerprint = null);

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
    public string? TranslatedDescription { get; set; }
}

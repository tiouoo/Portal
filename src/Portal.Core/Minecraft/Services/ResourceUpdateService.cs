using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flurl.Http;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Models;
using Portal.Core.Services;
using Portal.Localization;
using MinecraftLaunch.Utilities;

namespace Portal.Core.Minecraft.Services;

/// <summary>
/// 检查本地资源（模组/资源包/光影包）是否有可用更新，并提供更新与回滚执行能力。
/// 更新检测结果带 60 分钟内存缓存。
/// </summary>
public sealed class ResourceUpdateService
{
    private const string ModrinthVersionFilesEndpoint = "https://api.modrinth.com/v2/version_files";
    private const string ModrinthUpdateEndpoint = "https://api.modrinth.com/v2/version_files/update";
    private const string CurseForgeFingerprintEndpoint = "https://api.curseforge.com/v1/fingerprints";
    private const string CurseForgeModsEndpoint = "https://api.curseforge.com/v1/mods";
    private const int MaximumConcurrentRequests = 4;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(60);
    private static readonly ConcurrentDictionary<string, (ResourceUpdateResult Result, DateTime CheckedAt)> Cache =
        new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim NetworkSemaphore = new(MaximumConcurrentRequests);

    public static void InvalidateCache(string filePath)
    {
        foreach (var key in Cache.Keys.Where(key => KeyMatchesPath(key, filePath)).ToArray())
            Cache.TryRemove(key, out _);
    }

    public async Task<IReadOnlyDictionary<string, ResourceUpdateResult>> CheckUpdatesAsync(
        MinecraftInstance instance,
        IEnumerable<ResourceUpdateCandidate> candidates,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var gameVersion = instance.VersionId;
        var loaders = ResourceCompatibility.GetInstalledLoaders(instance);
        var results = new Dictionary<string, ResourceUpdateResult>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<(ResourceUpdateCandidate Candidate, string Key)>();
        foreach (var candidate in candidates)
        {
            var key = BuildKey(candidate, gameVersion, loaders);
            if (!forceRefresh && Cache.TryGetValue(key, out var cached) &&
                DateTime.UtcNow - cached.CheckedAt < CacheLifetime)
            {
                results[candidate.FilePath] = cached.Result;
                continue;
            }

            pending.Add((candidate, key));
        }

        if (pending.Count == 0)
            return results;

        var withHashes = await ResolveHashesAsync(pending.Select(item => item.Candidate).ToArray(), cancellationToken);
        var identified = await ResolveIdentitiesAsync(withHashes, cancellationToken);

        var modrinthByIdentity = identified.Where(item => item.Source == "Modrinth").ToArray();
        var curseForgeByIdentity = identified.Where(item => item.Source == "CurseForge").ToArray();

        var modrinthUpdates = await FetchModrinthUpdatesAsync(
            modrinthByIdentity.Where(item => item.Sha1 != null).Select(item => item.Sha1!).ToArray(),
            gameVersion, loaders, cancellationToken);
        var curseForgeUpdates = await FetchCurseForgeUpdatesAsync(
            curseForgeByIdentity.Where(item => item.Fingerprint != null).Select(item => item.Fingerprint!.Value).ToArray(),
            gameVersion, loaders, cancellationToken);

        foreach (var item in identified)
        {
            ResourceUpdateResult? result = null;
            if (item.Source == "Modrinth" && item.Sha1 != null &&
                modrinthUpdates.TryGetValue(item.Sha1, out var target))
                result = BuildResult(item, target, ModDetailsSource.Modrinth);
            else if (item.Source == "CurseForge" && item.Fingerprint != null &&
                     curseForgeUpdates.TryGetValue(item.Fingerprint!.Value, out var curseForgeTarget))
                result = BuildResult(item, curseForgeTarget, ModDetailsSource.CurseForge);

            result ??= new ResourceUpdateResult(item.Candidate.FilePath,
                item.Source == "Modrinth" ? ModDetailsSource.Modrinth :
                item.Source == "CurseForge" ? ModDetailsSource.CurseForge : null,
                item.ProjectId, item.VersionId, item.VersionId, null);

            var pair = pending.FirstOrDefault(p => string.Equals(p.Candidate.FilePath, item.Candidate.FilePath, StringComparison.OrdinalIgnoreCase));
            var key = pair.Key ?? BuildKey(item.Candidate, gameVersion, loaders);
            Cache[key] = (result, DateTime.UtcNow);
            results[item.Candidate.FilePath] = result;
        }

        return results;
    }

    /// <summary>应用更新：归档旧文件并把已下载到临时路径的新文件放入目标位置。返回新文件路径。</summary>
    public static string ApplyUpdateFile(string oldFilePath, string downloadedTempPath, string newFileName)
    {
        var folder = Path.GetDirectoryName(oldFilePath) ?? string.Empty;
        var baseName = ResourceBackupStore.NormalizeBase(newFileName);
        var disabled = Path.GetFileName(oldFilePath).EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
        var finalName = disabled ? baseName + ".disabled" : baseName;
        var finalPath = Path.Combine(folder, finalName);

        ResourceBackupStore.ArchiveForUpdate(oldFilePath, finalName);
        if (File.Exists(finalPath))
            File.Delete(finalPath);
        File.Move(downloadedTempPath, finalPath);
        try
        {
            File.SetLastWriteTimeUtc(finalPath, DateTime.UtcNow);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return finalPath;
    }

    private static ResourceUpdateResult BuildResult(
        (ResourceUpdateCandidate Candidate, string? Sha1, uint? Fingerprint, string? Source, string? ProjectId, string? VersionId) item,
        ModVersionFileItem target, ModDetailsSource source)
    {
        return new ResourceUpdateResult(item.Candidate.FilePath, source, item.ProjectId, item.VersionId,
            target.Id, target);
    }

    private static async Task<(ResourceUpdateCandidate Candidate, string? Sha1, uint? Fingerprint, string? Source, string? ProjectId, string? VersionId)[]>
        ResolveIdentitiesAsync(
            (ResourceUpdateCandidate Candidate, string? Sha1, uint? Fingerprint)[] items,
            CancellationToken cancellationToken)
    {
        var output = new List<(ResourceUpdateCandidate, string?, uint?, string?, string?, string?)>();
        var needIdentity = new List<(ResourceUpdateCandidate Candidate, string? Sha1, uint? Fingerprint)>();
        var pendingModrinthVersion = new List<(ResourceUpdateCandidate Candidate, string? Sha1, uint? Fingerprint)>();
        var pendingCurseForgeVersion = new List<(ResourceUpdateCandidate Candidate, string? Sha1, uint? Fingerprint)>();
        var kind = items.Length > 0 ? items[0].Candidate.Kind : ResourceKind.Mod;
        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.Candidate.Source) &&
                !string.IsNullOrEmpty(item.Candidate.ProjectId))
            {
                if (item.Candidate.Source == "Modrinth" && string.IsNullOrEmpty(item.Candidate.VersionId) &&
                    item.Sha1 != null)
                    pendingModrinthVersion.Add(item);
                else if (item.Candidate.Source == "CurseForge" && string.IsNullOrEmpty(item.Candidate.VersionId) &&
                         item.Fingerprint != null)
                    pendingCurseForgeVersion.Add(item);
                else
                    output.Add((item.Candidate, item.Sha1, item.Fingerprint, item.Candidate.Source,
                        item.Candidate.ProjectId, item.Candidate.VersionId));
                continue;
            }

            var cached = item.Sha1 is null ? null :
                kind == ResourceKind.Mod ? CacheDatabase.ReadMod(item.Sha1)
                                         : CacheDatabase.ReadResource(kind, item.Sha1);
            if (cached is { Source: "Modrinth" } && cached.ModrinthProjectId is { Length: > 0 } cachedProjectId)
            {
                if (string.IsNullOrEmpty(cached.ModrinthVersionId) && item.Sha1 != null)
                    pendingModrinthVersion.Add(item);
                else
                    output.Add((item.Candidate, item.Sha1, item.Fingerprint, "Modrinth", cachedProjectId,
                        cached.ModrinthVersionId));
                continue;
            }

            if (cached is { Source: "CurseForge", ProjectId: { } cachedCurseProjectId })
            {
                if (cached.FileId is null && item.Fingerprint != null)
                    pendingCurseForgeVersion.Add(item);
                else
                    output.Add((item.Candidate, item.Sha1, item.Fingerprint, "CurseForge",
                        cachedCurseProjectId.ToString(), cached.FileId?.ToString()));
                continue;
            }

            needIdentity.Add(item);
        }

        var modrinthHashes = needIdentity.Where(item => item.Sha1 != null).Select(item => item.Sha1!)
            .Concat(pendingModrinthVersion.Where(item => item.Sha1 != null).Select(item => item.Sha1!))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var modrinthIdentity = await ResolveModrinthIdentitiesAsync(modrinthHashes, kind, cancellationToken);

        var remaining = new List<(ResourceUpdateCandidate Candidate, string? Sha1, uint? Fingerprint)>();
        foreach (var item in needIdentity.Where(item => item.Sha1 != null))
        {
            if (modrinthIdentity.TryGetValue(item.Sha1!, out var identity))
            {
                output.Add((item.Candidate, item.Sha1, item.Fingerprint, "Modrinth", identity.ProjectId,
                    identity.VersionId));
                continue;
            }

            remaining.Add(item);
        }

        foreach (var item in pendingModrinthVersion)
        {
            output.Add((item.Candidate, item.Sha1, item.Fingerprint, "Modrinth",
                item.Candidate.ProjectId,
                item.Sha1 != null && modrinthIdentity.TryGetValue(item.Sha1, out var identity)
                    ? identity.VersionId
                    : null));
        }

        var fingerprintPool = needIdentity.Where(item => item.Fingerprint != null).Select(item => item.Fingerprint!.Value)
            .Concat(remaining.Where(item => item.Fingerprint != null).Select(item => item.Fingerprint!.Value))
            .Concat(pendingCurseForgeVersion.Where(item => item.Fingerprint != null).Select(item => item.Fingerprint!.Value))
            .Distinct().ToArray();
        var curseForgeIdentity = await ResolveCurseForgeIdentitiesAsync(fingerprintPool, cancellationToken);

        var resolvedPaths = output.Select(entry => entry.Item1.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in needIdentity)
        {
            if (resolvedPaths.Contains(item.Candidate.FilePath))
                continue;
            if (item.Fingerprint is { } fingerprint && curseForgeIdentity.TryGetValue(fingerprint, out var identity))
            {
                CacheCurseForgeIdentity(kind, fingerprint, item.Sha1, identity);
                output.Add((item.Candidate, item.Sha1, item.Fingerprint, "CurseForge",
                    identity.ProjectId.ToString(), identity.FileId?.ToString()));
            }
            else
                output.Add((item.Candidate, item.Sha1, item.Fingerprint, null, null, null));
        }

        foreach (var item in pendingCurseForgeVersion)
        {
            var fileId = item.Fingerprint is { } fingerprint &&
                         curseForgeIdentity.TryGetValue(fingerprint, out var identity)
                ? identity.FileId
                : null;
            if (item.Fingerprint is { } cacheFingerprint && curseForgeIdentity.TryGetValue(cacheFingerprint, out var curseForgeIdentityValue))
                CacheCurseForgeIdentity(kind, cacheFingerprint, item.Sha1, curseForgeIdentityValue);
            output.Add((item.Candidate, item.Sha1, item.Fingerprint, "CurseForge",
                item.Candidate.ProjectId, fileId?.ToString()));
        }

        return output.ToArray();
    }

    private static void CacheCurseForgeIdentity(ResourceKind kind, uint fingerprint, string? sha1,
        (int ProjectId, int? FileId) identity)
    {
        var identityEntry = new ModCacheEntry
        {
            MetadataFetched = true,
            Source = "CurseForge",
            ProjectId = identity.ProjectId,
            FileId = identity.FileId
        };
        if (kind != ResourceKind.Mod)
        {
            CacheDatabase.WriteResource(kind, fingerprint, sha1, identityEntry);
            return;
        }

        var existing = sha1 != null ? CacheDatabase.ReadMod(sha1) : CacheDatabase.ReadMod(fingerprint);
        var entry = existing == null ? identityEntry : identityEntry with
        {
            DisplayName = existing.DisplayName,
            Description = existing.Description,
            IconUrl = existing.IconUrl,
            FriendlyName = existing.FriendlyName,
            CurseForgeSlug = existing.CurseForgeSlug,
            IsWikiFriendlyName = existing.IsWikiFriendlyName,
            ModrinthSlug = existing.ModrinthSlug,
            TranslatedDescription = existing.TranslatedDescription
        };
        if (sha1 is null)
            CacheDatabase.WriteMod(fingerprint, entry);
        else
            CacheDatabase.WriteMod(fingerprint, sha1, entry);
    }

    private static async Task<Dictionary<string, (string ProjectId, string VersionId)>>
        ResolveModrinthIdentitiesAsync(string[] hashes, ResourceKind kind, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        if (hashes.Length == 0)
            return result;

        foreach (var batch in hashes.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(50))
        {
            try
            {
                await NetworkSemaphore.WaitAsync(cancellationToken);
                try
                {
                    var response = await HttpUtil.Request(ModrinthVersionFilesEndpoint)
                        .WithHeader("Accept", "application/json")
                        .PostJsonAsync(new { hashes = batch, algorithm = "sha1" }, cancellationToken: cancellationToken)
                        .ReceiveJson<Dictionary<string, ModrinthIdentityVersion>>();
                    foreach (var hash in batch)
                    {
                        if (response.TryGetValue(hash, out var version) && version is { Id: { Length: > 0 }, ProjectId: { Length: > 0 } })
                        {
                            result[hash] = (version.ProjectId, version.Id);
                            var entry = new ModCacheEntry
                            {
                                MetadataFetched = true,
                                Source = "Modrinth",
                                ModrinthProjectId = version.ProjectId,
                                ModrinthVersionId = version.Id
                            };
                            if (kind == ResourceKind.Mod)
                                CacheDatabase.WriteMod(hash, entry);
                            else
                                CacheDatabase.WriteResource(kind, null, hash, entry);
                        }
                    }
                }
                finally
                {
                    NetworkSemaphore.Release();
                }
            }
            catch (FlurlHttpException)
            {
            }
            catch (JsonException)
            {
            }
        }

        return result;
    }

    private static async Task<Dictionary<uint, (int ProjectId, int? FileId)>>
        ResolveCurseForgeIdentitiesAsync(uint[] fingerprints, CancellationToken cancellationToken)
    {
        var result = new Dictionary<uint, (int, int?)>();
        if (fingerprints.Length == 0 || CredentialsService.CurseForgeApiKey is null)
            return result;

        foreach (var batch in fingerprints.Distinct().Chunk(50))
        {
            try
            {
                await NetworkSemaphore.WaitAsync(cancellationToken);
                try
                {
                    var response = await HttpUtil.Request(CurseForgeFingerprintEndpoint)
                        .WithHeader("Accept", "application/json")
                        .WithHeader("x-api-key", CredentialsService.CurseForgeApiKey!)
                        .PostJsonAsync(new { fingerprints = batch }, cancellationToken: cancellationToken)
                        .ReceiveJson<ResCurseForgeFingerprintResponse>();
                    foreach (var match in response.Data?.ExactMatches ?? [])
                    {
                        if (match.File is null)
                            continue;
                        result[match.File.Fingerprint] = (match.File.ModId, match.File.Id);
                    }
                }
                finally
                {
                    NetworkSemaphore.Release();
                }
            }
            catch (FlurlHttpException)
            {
            }
            catch (JsonException)
            {
            }
        }

        return result;
    }

    private static async Task<Dictionary<string, ModVersionFileItem>> FetchModrinthUpdatesAsync(
        string[] hashes, string gameVersion, IReadOnlyList<string> loaders, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, ModVersionFileItem>(StringComparer.OrdinalIgnoreCase);
        if (hashes.Length == 0)
            return result;

        foreach (var batch in hashes.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(50))
        {
            try
            {
                await NetworkSemaphore.WaitAsync(cancellationToken);
                try
                {
                    var response = await HttpUtil.Request(ModrinthUpdateEndpoint)
                        .WithHeader("Accept", "application/json")
                        .PostJsonAsync(new
                        {
                            hashes = batch,
                            algorithm = "sha1",
                            game_versions = new[] { gameVersion },
                            loaders
                        }, cancellationToken: cancellationToken)
                        .ReceiveJson<Dictionary<string, ModrinthUpdateVersion>>();
                    foreach (var hash in batch)
                    {
                        if (response.TryGetValue(hash, out var version))
                            result[hash] = version.ToFileItem();
                    }
                }
                finally
                {
                    NetworkSemaphore.Release();
                }
            }
            catch (FlurlHttpException)
            {
            }
            catch (JsonException)
            {
            }
        }

        return result;
    }

    private static async Task<Dictionary<uint, ModVersionFileItem>> FetchCurseForgeUpdatesAsync(
        uint[] fingerprints, string gameVersion, IReadOnlyList<string> loaders, CancellationToken cancellationToken)
    {
        var result = new Dictionary<uint, ModVersionFileItem>();
        if (fingerprints.Length == 0 || CredentialsService.CurseForgeApiKey is null)
            return result;

        var matches = new Dictionary<uint, (int ModId, int CurrentFileId)>();
        foreach (var batch in fingerprints.Distinct().Chunk(50))
        {
            try
            {
                await NetworkSemaphore.WaitAsync(cancellationToken);
                try
                {
                    var response = await HttpUtil.Request(CurseForgeFingerprintEndpoint)
                        .WithHeader("Accept", "application/json")
                        .WithHeader("x-api-key", CredentialsService.CurseForgeApiKey!)
                        .PostJsonAsync(new { fingerprints = batch }, cancellationToken: cancellationToken)
                        .ReceiveJson<ResCurseForgeFingerprintResponse>();
                    foreach (var match in response.Data?.ExactMatches ?? [])
                    {
                        if (match.File is null)
                            continue;
                        matches[match.File.Fingerprint] = (match.File.ModId, match.File.Id);
                    }
                }
                finally
                {
                    NetworkSemaphore.Release();
                }
            }
            catch (FlurlHttpException)
            {
            }
            catch (JsonException)
            {
            }
        }

        foreach (var (modId, currentFileId) in matches.Values.Distinct())
        {
            var target = await FindLatestCurseForgeFileAsync(modId, currentFileId, gameVersion, loaders,
                cancellationToken);
            if (target is null)
                continue;
            foreach (var pair in matches.Where(pair => pair.Value.ModId == modId && pair.Value.CurrentFileId == currentFileId))
                result[pair.Key] = target;
        }

        return result;
    }

    private static async Task<ModVersionFileItem?> FindLatestCurseForgeFileAsync(int modId, int currentFileId,
        string gameVersion, IReadOnlyList<string> loaders, CancellationToken cancellationToken)
    {
        try
        {
            await NetworkSemaphore.WaitAsync(cancellationToken);
            try
            {
                var response = await HttpUtil.Request($"{CurseForgeModsEndpoint}/{modId}")
                    .WithHeader("Accept", "application/json")
                    .WithHeader("x-api-key", CredentialsService.CurseForgeApiKey!)
                    .GetJsonAsync<ResCurseForgeModResponse>(cancellationToken: cancellationToken);
                var data = response.Data;
                if (data?.LatestFilesIndexes is null)
                    return null;

                var targetIndex = data.LatestFilesIndexes
                    .Where(index => index.GameVersion == gameVersion)
                    .Where(index => loaders.Count == 0 || index.ModLoader == 0 ||
                                    loaders.Contains(LoaderName(index.ModLoader)))
                    .Where(index => index.FileId != currentFileId)
                    .OrderByDescending(index => index.FileId)
                    .FirstOrDefault();
                if (targetIndex is null)
                    return null;

                var file = data.LatestFiles?.FirstOrDefault(candidate => candidate.Id == targetIndex.FileId);
                file ??= await FetchCurseForgeFileAsync(modId, targetIndex.FileId, cancellationToken);
                return file is null ? null : file.ToFileItem();
            }
            finally
            {
                NetworkSemaphore.Release();
            }
        }
        catch (FlurlHttpException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<ResCurseForgeFile?> FetchCurseForgeFileAsync(int modId, int fileId,
        CancellationToken cancellationToken)
    {
        try
        {
            await NetworkSemaphore.WaitAsync(cancellationToken);
            try
            {
                var response = await HttpUtil.Request($"{CurseForgeModsEndpoint}/{modId}/files/{fileId}")
                    .WithHeader("Accept", "application/json")
                    .WithHeader("x-api-key", CredentialsService.CurseForgeApiKey!)
                    .GetJsonAsync<ResCurseForgeFileResponse>(cancellationToken: cancellationToken);
                return response.Data;
            }
            finally
            {
                NetworkSemaphore.Release();
            }
        }
        catch (FlurlHttpException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<(ResourceUpdateCandidate Candidate, string? Sha1, uint? Fingerprint)[]> ResolveHashesAsync(
        ResourceUpdateCandidate[] candidates, CancellationToken cancellationToken)
    {
        var output = new List<(ResourceUpdateCandidate, string?, uint?)>();
        foreach (var candidate in candidates)
        {
            if (candidate.Sha1 != null || candidate.Fingerprint != null)
            {
                output.Add((candidate, candidate.Sha1, candidate.Fingerprint));
                continue;
            }

            try
            {
                var hashes = await Task.Run(() => ModService.ComputeHashes(candidate.FilePath, cancellationToken),
                    cancellationToken);
                output.Add((candidate, hashes.Sha1, hashes.Fingerprint));
            }
            catch (IOException)
            {
                output.Add((candidate, null, null));
            }
            catch (UnauthorizedAccessException)
            {
                output.Add((candidate, null, null));
            }
        }

        return output.ToArray();
    }

    private static string BuildKey(ResourceUpdateCandidate candidate, string gameVersion, IReadOnlyList<string> loaders)
    {
        return $"{candidate.FilePath}|{gameVersion}|{string.Join(",", loaders)}|{candidate.Kind}";
    }

    private static bool KeyMatchesPath(string key, string filePath)
    {
        var separator = key.IndexOf('|');
        return separator > 0 && string.Equals(key[..separator], filePath, StringComparison.OrdinalIgnoreCase);
    }

    private static string? LoaderName(int modLoader)
    {
        return modLoader switch
        {
            1 => "forge",
            4 => "fabric",
            5 => "quilt",
            6 => "neoforge",
            _ => null
        };
    }

    private sealed class ModrinthIdentityVersion
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("project_id")] public string? ProjectId { get; init; }
    }

    private sealed class ModrinthUpdateVersion
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("project_id")] public string? ProjectId { get; init; }
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("version_number")] public string? VersionNumber { get; init; }
        [JsonPropertyName("date_published")] public DateTime Published { get; init; }
        [JsonPropertyName("version_type")] public string? VersionType { get; init; }
        [JsonPropertyName("game_versions")] public List<string>? GameVersions { get; init; }
        [JsonPropertyName("loaders")] public List<string>? Loaders { get; init; }
        [JsonPropertyName("files")] public List<ModrinthUpdateFile>? Files { get; init; }

        public ModVersionFileItem ToFileItem()
        {
            var file = Files?.FirstOrDefault(candidate => candidate.IsPrimary) ?? Files?.FirstOrDefault();
            var minecraftVersions = GameVersions?.ToArray() ?? [];
            var loaderKeys = Loaders?.Where(loader => loader is not null)
                .SelectMany(loader => minecraftVersions.Select(version => new ModVersionGroupKey(LoaderDisplayName(loader), version)))
                .ToList() ?? [];
            return new ModVersionFileItem(
                Id ?? string.Empty,
                string.IsNullOrWhiteSpace(Name) ? file?.FileName ?? string.Empty : Name,
                FormatDetails(loaderKeys.Count > 0 ? string.Join(",", loaderKeys.Select(key => key.Loader).Distinct()) : LinguaSentinels.UniversalLoader,
                    file?.FileName ?? string.Empty, Published, ReleaseType(VersionType)),
                ReleaseType(VersionType),
                file?.FileName ?? string.Empty,
                file?.Url ?? string.Empty,
                file?.Size ?? 0,
                Published,
                minecraftVersions,
                loaderKeys,
                ModDetailsSource.Modrinth,
                ProjectId ?? string.Empty,
                []);
        }

        private static string LoaderDisplayName(string loader)
        {
            return loader.ToLowerInvariant() switch
            {
                "neoforge" => "NeoForge",
                "forge" => "Forge",
                "fabric" => "Fabric",
                "quilt" => "Quilt",
                _ => LinguaSentinels.UniversalLoader
            };
        }

        private static string ReleaseType(string? versionType)
        {
            return versionType switch
            {
                "release" => CommonLanguageManager.Instance.mod_releaseTypeRelease.CurrentValue(),
                "beta" => CommonLanguageManager.Instance.mod_releaseTypeBeta.CurrentValue(),
                "alpha" => CommonLanguageManager.Instance.mod_releaseTypeAlpha.CurrentValue(),
                _ => CommonLanguageManager.Instance.mod_releaseTypeOther.CurrentValue()
            };
        }

        private static string FormatDetails(string loader, string fileName, DateTime published, string releaseType)
        {
            return $"{loader}·{fileName}·{Portal.Core.App.Helpers.RelativeTime.Format(published)}·{releaseType}";
        }
    }

    private sealed class ModrinthUpdateFile
    {
        [JsonPropertyName("filename")] public string? FileName { get; init; }
        [JsonPropertyName("url")] public string? Url { get; init; }
        [JsonPropertyName("size")] public long Size { get; init; }
        [JsonPropertyName("primary")] public bool IsPrimary { get; init; }
    }

    private sealed class ResCurseForgeFingerprintResponse
    {
        [JsonPropertyName("data")] public ResCurseForgeFingerprintData? Data { get; init; }
    }

    private sealed class ResCurseForgeFingerprintData
    {
        [JsonPropertyName("exactMatches")] public List<ResCurseForgeMatch>? ExactMatches { get; init; }
    }

    private sealed class ResCurseForgeMatch
    {
        [JsonPropertyName("file")] public ResCurseForgeFile? File { get; init; }
    }

    private sealed class ResCurseForgeFile
    {
        [JsonPropertyName("id")] public int Id { get; init; }
        [JsonPropertyName("modId")] public int ModId { get; init; }
        [JsonPropertyName("fileFingerprint")] public uint Fingerprint { get; init; }
        [JsonPropertyName("fileName")] public string? FileName { get; init; }
        [JsonPropertyName("displayName")] public string? DisplayName { get; init; }
        [JsonPropertyName("fileDate")] public DateTime Published { get; init; }
        [JsonPropertyName("releaseType")] public int ReleaseType { get; init; }
        [JsonPropertyName("downloadUrl")] public string? DownloadUrl { get; init; }
        [JsonPropertyName("fileLength")] public long FileLength { get; init; }
        [JsonPropertyName("gameVersions")] public List<string>? GameVersions { get; init; }
        [JsonPropertyName("isAvailable")] public bool IsAvailable { get; init; }

        public ModVersionFileItem ToFileItem()
        {
            var versions = GameVersions?.Where(IsMinecraftVersion).ToList() ?? [];
            var loaders = GameVersions?.Select(LoaderName).OfType<string>().DefaultIfEmpty(LinguaSentinels.UniversalLoader).Distinct().ToArray() ?? [LinguaSentinels.UniversalLoader];
            var loaderKeys = loaders.SelectMany(loader => versions.Select(version => new ModVersionGroupKey(loader, version)))
                .ToList();
            return new ModVersionFileItem(Id.ToString(),
                string.IsNullOrWhiteSpace(DisplayName) ? FileName ?? string.Empty : DisplayName,
                $"{string.Join(",", loaders)}·{FileName ?? string.Empty}·{Portal.Core.App.Helpers.RelativeTime.Format(Published)}·{ReleaseTypeText(ReleaseType)}",
                ReleaseTypeText(ReleaseType), FileName ?? string.Empty, DownloadUrl ?? string.Empty, FileLength, Published,
                versions, loaderKeys, ModDetailsSource.CurseForge, ModId.ToString(), []);
        }

        private static string? LoaderName(string loader)
        {
            return loader.Trim().ToLowerInvariant() switch
            {
                "forge" => "Forge",
                "fabric" => "Fabric",
                "quilt" => "Quilt",
                "neoforge" => "NeoForge",
                _ => null
            };
        }

        private static bool IsMinecraftVersion(string version)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(version,
                @"^\d+\.\d+(?:\.\d+)?(?:-(?:snapshot|pre-release|pre\d+|rc\d+))?$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static string ReleaseTypeText(int releaseType)
        {
            return releaseType switch
            {
                1 => CommonLanguageManager.Instance.mod_releaseTypeRelease.CurrentValue(),
                2 => CommonLanguageManager.Instance.mod_releaseTypeBeta.CurrentValue(),
                3 => CommonLanguageManager.Instance.mod_releaseTypeAlpha.CurrentValue(),
                _ => CommonLanguageManager.Instance.mod_releaseTypeOther.CurrentValue()
            };
        }
    }

    private sealed class ResCurseForgeModResponse
    {
        [JsonPropertyName("data")] public ResCurseForgeMod? Data { get; init; }
    }

    private sealed class ResCurseForgeMod
    {
        [JsonPropertyName("latestFiles")] public List<ResCurseForgeFile>? LatestFiles { get; init; }
        [JsonPropertyName("latestFilesIndexes")] public List<ResCurseForgeFileIndex>? LatestFilesIndexes { get; init; }
    }

    private sealed class ResCurseForgeFileIndex
    {
        [JsonPropertyName("gameVersion")] public string? GameVersion { get; init; }
        [JsonPropertyName("modLoader")] public int ModLoader { get; init; }
        [JsonPropertyName("fileId")] public int FileId { get; init; }
    }

    private sealed class ResCurseForgeFileResponse
    {
        [JsonPropertyName("data")] public ResCurseForgeFile? Data { get; init; }
    }
}

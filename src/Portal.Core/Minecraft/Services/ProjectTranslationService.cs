using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flurl.Http;

namespace Portal.Core.Minecraft.Services;

public enum ProjectTranslationSource
{
    CurseForge,
    Modrinth
}

public static class ProjectTranslationService
{
    private const string ApiRoot = "https://mod.mcimirror.top/translate";
    private const int BatchSize = 50;
    private const int MaximumConcurrentRequests = 4;
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    public static async Task<IReadOnlyDictionary<string, string>> GetTranslationsAsync(
        ProjectTranslationSource source, IEnumerable<string> projectIds, CancellationToken cancellationToken = default)
    {
        var requested = projectIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)
            .ToArray();
        var results = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var id in requested)
            if (Cache.TryGetValue(GetCacheKey(source, id), out var translated))
            {
                if (!string.IsNullOrWhiteSpace(translated)) results[id] = translated;
            }
            else
            {
                missing.Add(id);
            }

        using var semaphore = new SemaphoreSlim(MaximumConcurrentRequests);
        await Task.WhenAll(missing.Chunk(BatchSize).Select(async batch =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var translations = await FetchBatchAsync(source, batch, cancellationToken);
                if (translations == null) return;

                foreach (var id in batch) Cache.TryAdd(GetCacheKey(source, id), string.Empty);
                foreach (var translation in translations)
                {
                    if (string.IsNullOrWhiteSpace(translation.ProjectId) ||
                        string.IsNullOrWhiteSpace(translation.Translated))
                        continue;
                    Cache[GetCacheKey(source, translation.ProjectId)] = translation.Translated;
                    results[translation.ProjectId] = translation.Translated;
                }
            }
            finally
            {
                semaphore.Release();
            }
        }));
        return results;
    }

    private static async Task<IReadOnlyList<ProjectTranslation>?> FetchBatchAsync(ProjectTranslationSource source,
        string[] projectIds, CancellationToken cancellationToken)
    {
        try
        {
            if (source == ProjectTranslationSource.CurseForge)
            {
                var ids = projectIds.Select(id => int.TryParse(id, out var value) ? (int?)value : null)
                    .Where(id => id.HasValue).Select(id => id!.Value).ToArray();
                if (ids.Length == 0) return [];
                var response = await $"{ApiRoot}/curseforge".WithHeader("Accept", "application/json")
                    .WithTimeout(TimeSpan.FromSeconds(8))
                    .PostJsonAsync(new { modids = ids }, cancellationToken: cancellationToken)
                    .ReceiveJson<List<CurseForgeTranslation>>();
                return response.Select(item => new ProjectTranslation(item.ModId.ToString(), item.Translated))
                    .ToArray();
            }

            var modrinthResponse = await $"{ApiRoot}/modrinth".WithHeader("Accept", "application/json")
                .WithTimeout(TimeSpan.FromSeconds(8))
                .PostJsonAsync(new { project_ids = projectIds }, cancellationToken: cancellationToken)
                .ReceiveJson<List<ModrinthTranslation>>();
            return modrinthResponse.Select(item => new ProjectTranslation(item.ProjectId, item.Translated)).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FlurlHttpException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string GetCacheKey(ProjectTranslationSource source, string projectId)
    {
        return $"{source}:{projectId}";
    }

    private sealed record ProjectTranslation(string ProjectId, string? Translated);

    private sealed record CurseForgeTranslation(
        [property: JsonPropertyName("modid")] int ModId,
        [property: JsonPropertyName("translated")]
        string? Translated);

    private sealed record ModrinthTranslation(
        [property: JsonPropertyName("project_id")]
        string ProjectId,
        [property: JsonPropertyName("translated")]
        string? Translated);
}
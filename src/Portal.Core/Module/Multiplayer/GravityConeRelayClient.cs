using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Portal.Core.Const;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Module.Multiplayer;

public sealed class GravityConeRelayClient
{
    public const string DefaultRelaySourceUrl = "https://portal.tiouo.cc/relays.json";
    private const int CacheSchemaVersion = 2;

    private const int MaxResponseSizeBytes = 1 * 1024 * 1024;
    private const int MaxSourceDepth = 8;
    private const int MaxSourceCount = 128;

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly SemaphoreSlim UpdateLock = new(1, 1);

    public static readonly GravityConeRelayClient Instance = new();
    private static string CachePath => Path.Combine(ConfigPath.UserDataRootPath, "Multiplayer", "portal-relays.json");

    /// <summary>
    /// 用户配置的中央节点内容：每行一个链接或节点地址。
    /// http/https 链接会在更新时获取其中的 peers 列表；tcp/udp/ws 等地址会直接作为节点使用。
    /// </summary>
    public static string ConfiguredSourcesText
    {
        get => Data.ConfigEntry.GravityConeRelaySources;
        set => Data.ConfigEntry.GravityConeRelaySources = value;
    }

    public async Task PrefetchAsync(CancellationToken cancellationToken = default)
    {
        if (Data.ConfigEntry.GravityConeRelayAutoUpdate)
        {
            try
            {
                var updatedRelays = await UpdateRelaySourcesAsync(cancellationToken);
                Logger.Info(string.Format(LogLanguageManager.Instance.multiplayer_relaysPrefetched.CurrentValue(), updatedRelays.Count));
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Logger.Warning(string.Format(LogLanguageManager.Instance.multiplayer_relaysUpdateFailed.CurrentValue(),
                    Environment.NewLine, exception));
            }
        }

        var relays = await ReadRequiredCacheAsync(cancellationToken);
        Logger.Info(string.Format(LogLanguageManager.Instance.multiplayer_relaysPrefetched.CurrentValue(), relays.Count));
    }

    /// <summary>
    /// 从配置的所有来源获取并合并节点列表（用于联机客户端）。
    /// </summary>
    public async Task<IReadOnlyList<string>> FetchRelaysAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await UpdateRelaySourcesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.Warning(string.Format(LogLanguageManager.Instance.multiplayer_fetchRelaysFailed.CurrentValue(),
                Environment.NewLine, exception));
            return await ReadRequiredCacheAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 获取所有 http/https 来源的节点列表，与用户直接填写的节点合并、去重并写入缓存。
    /// 来源配置本身保持不变，加载结果由界面单独展示。
    /// </summary>
    public async Task<IReadOnlyList<string>> UpdateRelaySourcesAsync(CancellationToken cancellationToken)
    {
        await UpdateLock.WaitAsync(cancellationToken);
        try
        {
            var (sources, directPeers) = ParseConfiguredSources();
            var result = new List<string>(directPeers);
            var visitedSources = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in sources)
            {
                try
                {
                    await ResolveSourceAsync(source, result, visitedSources, 0, cancellationToken);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    Logger.Warning(string.Format(
                        LogLanguageManager.Instance.multiplayer_relaySourceFailed.CurrentValue(), source,
                        Environment.NewLine, exception));
                }
            }

            result = result.Where(IsFinalPeer).Distinct(StringComparer.Ordinal).ToList();
            if (result.Count == 0)
                throw new InvalidDataException(CommonLanguageManager.Instance.multiplayer_relaysNoUsableNodes.CurrentValue());

            await SaveCacheAsync(result, cancellationToken);
            return result;
        }
        finally
        {
            UpdateLock.Release();
        }
    }

    /// <summary>Returns the shared public relay set used by all multiplayer backends.</summary>
    public async Task<IReadOnlyList<string>> GetAvailableRelaysAsync(CancellationToken cancellationToken)
        => await FetchRelaysAsync(cancellationToken);

    /// <summary>
    /// 解析用户配置文本：拆分为 http/https 来源链接与直接节点地址。
    /// </summary>
    public static (List<string> Sources, List<string> DirectPeers) ParseConfiguredSources()
        => ParseConfiguredSources(ConfiguredSourcesText);

    private static (List<string> Sources, List<string> DirectPeers) ParseConfiguredSources(string text)
    {
        var sources = new List<string>();
        var directPeers = new List<string>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
            if (Uri.TryCreate(line, UriKind.Absolute, out var uri) &&
                uri.Scheme is "http" or "https")
                sources.Add(line);
            else if (GravityConeNodeClient.IsValidPeer(line))
                directPeers.Add(line);
        }

        return (sources, directPeers);
    }

    private static async Task ResolveSourceAsync(string url, List<string> result, HashSet<string> visitedSources,
        int depth, CancellationToken cancellationToken)
    {
        if (depth >= MaxSourceDepth || visitedSources.Count >= MaxSourceCount || !visitedSources.Add(url)) return;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response =
            await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await ReadLimitedAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken);
        foreach (var peer in ParseSourceResponse(body))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Uri.TryCreate(peer, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                try
                {
                    await ResolveSourceAsync(peer, result, visitedSources, depth + 1, cancellationToken);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    Logger.Warning(string.Format(
                        LogLanguageManager.Instance.multiplayer_relaySourceFailed.CurrentValue(), peer,
                        Environment.NewLine, exception));
                }
            }
            else if (GravityConeNodeClient.IsValidPeer(peer))
                result.Add(peer);
        }
    }

    private static IReadOnlyList<string> ParseSourceResponse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (!root.TryGetProperty("peers", out var peers) || peers.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException(CommonLanguageManager.Instance.multiplayer_relaysMissingPeers.CurrentValue());

            return peers.EnumerateArray()
                .Where(peer => peer.ValueKind == JsonValueKind.String)
                .Select(peer => peer.GetString()?.Trim())
                .Where(peer => !string.IsNullOrWhiteSpace(peer))
                .Cast<string>()
                .ToList();
        }
        catch (JsonException)
        {
            return body.Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#') && !line.StartsWith(';'))
                .ToList();
        }
    }

    public async Task<IReadOnlyList<string>?> TryReadCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(CachePath)) return null;
        try
        {
            await using var stream = File.OpenRead(CachePath);
            var cache = await JsonSerializer.DeserializeAsync<RelayCache>(stream, JsonOptions, cancellationToken);
            if (cache is not { SchemaVersion: CacheSchemaVersion } || cache.Peers is not { Count: > 0 })
            {
                DeleteCache();
                return null;
            }

            if (cache.Peers.All(IsFinalPeer)) return cache.Peers;
            DeleteCache();
            return null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Logger.Warning(string.Format(LogLanguageManager.Instance.multiplayer_readRelayCacheFailed.CurrentValue(), Environment.NewLine, ex));
            return null;
        }
    }

    private async Task<IReadOnlyList<string>> ReadRequiredCacheAsync(CancellationToken cancellationToken)
        => await TryReadCacheAsync(cancellationToken)
           ?? throw new InvalidDataException(CommonLanguageManager.Instance.multiplayer_relaysNoUsableNodes.CurrentValue());

    private static bool IsFinalPeer(string peer)
        => GravityConeNodeClient.IsValidPeer(peer) &&
           Uri.TryCreate(peer, UriKind.Absolute, out var uri) &&
           uri.Scheme is not ("http" or "https");

    private static void DeleteCache()
    {
        try
        {
            File.Delete(CachePath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task<string> ReadLimitedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memory.Length + read > MaxResponseSizeBytes)
                throw new InvalidDataException(CommonLanguageManager.Instance.multiplayer_relayResponseTooLarge.CurrentValue());
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return Encoding.UTF8.GetString(memory.ToArray());
    }

    private static async Task SaveCacheAsync(IReadOnlyList<string> peers, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        var payload = JsonSerializer.Serialize(new { schemaVersion = CacheSchemaVersion, peers });
        await File.WriteAllTextAsync(CachePath, payload, cancellationToken);
    }

    private sealed class RelayCache
    {
            [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
        [JsonPropertyName("peers")] public List<string>? Peers { get; set; }
    }
}

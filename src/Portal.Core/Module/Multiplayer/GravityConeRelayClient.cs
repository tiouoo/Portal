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

    private const int MaxResponseSizeBytes = 1 * 1024 * 1024;

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static readonly GravityConeRelayClient Instance = new();
    private static string CachePath => Path.Combine(ConfigPath.UserDataRootPath, "Multiplayer", "portal-relays.json");

    /// <summary>
    /// 用户配置的中央节点内容：每行一个链接或节点地址。
    /// http/https 链接会在更新时获取其中的 peers 列表；tcp/udp/ws 等地址会直接作为节点使用。
    /// </summary>
    public static string ConfiguredSourcesText
    {
        get
        {
            var text = Data.ConfigEntry.GravityConeRelaySources;
            return string.IsNullOrWhiteSpace(text) ? DefaultRelaySourceUrl : text;
        }
        set => Data.ConfigEntry.GravityConeRelaySources = value;
    }

    public async Task PrefetchAsync(CancellationToken cancellationToken = default)
    {
        if (Data.ConfigEntry.GravityConeRelayAutoUpdate)
        {
            try
            {
                var text = await UpdateRelaySourcesAsync(cancellationToken);
                var (_, directPeers) = ParseConfiguredSources(text);
                var updatedRelays = directPeers.Where(GravityConeNodeClient.IsValidPeer).Distinct(StringComparer.Ordinal).ToList();
                if (updatedRelays.Count == 0)
                    throw new InvalidDataException(CommonLanguageManager.Instance.multiplayer_relaysNoUsableNodes.CurrentValue());
                await SaveCacheAsync(updatedRelays, cancellationToken);
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

        var relays = await FetchRelaysAsync(cancellationToken);
        Logger.Info(string.Format(LogLanguageManager.Instance.multiplayer_relaysPrefetched.CurrentValue(), relays.Count));
    }

    /// <summary>
    /// 从配置的所有来源获取并合并节点列表（用于联机客户端）。
    /// </summary>
    public async Task<IReadOnlyList<string>> FetchRelaysAsync(CancellationToken cancellationToken)
    {
        var (sources, directPeers) = ParseConfiguredSources();
        var result = new List<string>(directPeers);
        foreach (var source in sources)
        {
            try
            {
                result.AddRange(await FetchPeerListFromSourceAsync(source, cancellationToken));
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                Logger.Warning(string.Format(
                    LogLanguageManager.Instance.multiplayer_relaySourceFailed.CurrentValue(), source,
                    Environment.NewLine, exception));
            }
        }

        result = result.Where(GravityConeNodeClient.IsValidPeer).Distinct(StringComparer.Ordinal).ToList();
        if (result.Count == 0)
            throw new InvalidDataException(CommonLanguageManager.Instance.multiplayer_relaysNoUsableNodes.CurrentValue());

        await SaveCacheAsync(result, cancellationToken);
        return result;
    }

    /// <summary>Returns the shared public relay set used by all multiplayer backends.</summary>
    public async Task<IReadOnlyList<string>> GetAvailableRelaysAsync(CancellationToken cancellationToken)
        => await TryReadCacheAsync(cancellationToken) ?? await FetchRelaysAsync(cancellationToken);

    /// <summary>
    /// 获取所有 http/https 来源的节点列表，并与用户已填写的内容合并（追加、去重），
    /// 更新到配置输入框中，返回合并后的文本。不会覆盖用户自己填写的内容。
    /// </summary>
    public async Task<string> UpdateRelaySourcesAsync(CancellationToken cancellationToken)
    {
        var (sources, directPeers) = ParseConfiguredSources();
        var fetched = new List<string>();
        foreach (var source in sources)
        {
            try
            {
                fetched.AddRange(await FetchPeerListFromSourceAsync(source, cancellationToken));
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                Logger.Warning(string.Format(
                    LogLanguageManager.Instance.multiplayer_relaySourceFailed.CurrentValue(), source,
                    Environment.NewLine, exception));
            }
        }

        var merged = new List<string>();
        merged.AddRange(sources);
        merged.AddRange(directPeers);
        merged.AddRange(fetched);
        merged = merged.Distinct(StringComparer.Ordinal).ToList();

        var text = string.Join(Environment.NewLine, merged);
        ConfiguredSourcesText = text;
        return text;
    }

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

    private static async Task<IReadOnlyList<string>> FetchPeerListFromSourceAsync(string url,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response =
            await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await ReadLimitedAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("peers", out var peers) || peers.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException(CommonLanguageManager.Instance.multiplayer_relaysMissingPeers.CurrentValue());

        var result = new List<string>();
        foreach (var peer in peers.EnumerateArray())
        {
            if (peer.ValueKind != JsonValueKind.String) continue;
            var value = peer.GetString();
            if (!string.IsNullOrWhiteSpace(value) && GravityConeNodeClient.IsValidPeer(value))
                result.Add(value);
        }

        return result.Distinct(StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyList<string>?> TryReadCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(CachePath)) return null;
        try
        {
            await using var stream = File.OpenRead(CachePath);
            var cache = await JsonSerializer.DeserializeAsync<RelayCache>(stream, JsonOptions, cancellationToken);
            if (cache is not { SchemaVersion: 1 } || cache.Peers is not { Count: > 0 }) return null;
            return cache.Peers.All(GravityConeNodeClient.IsValidPeer) ? cache.Peers : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Logger.Warning(string.Format(LogLanguageManager.Instance.multiplayer_readRelayCacheFailed.CurrentValue(), Environment.NewLine, ex));
            return null;
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
        var payload = JsonSerializer.Serialize(new { schemaVersion = 1, peers });
        await File.WriteAllTextAsync(CachePath, payload, cancellationToken);
    }

    private sealed class RelayCache
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
        [JsonPropertyName("peers")] public List<string>? Peers { get; set; }
    }
}

using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Portal.Const;
using Portal.Core;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Module.Multiplayer;

/// <summary>
///     从 1TMC Uptime 获取联机组件使用的节点列表。
///     只使用 P2P 发现节点，不使用内置节点、不获取中继节点。
/// </summary>
public sealed class GravityConeNodeClient
{
    public const string UptimeBaseUrl = "https://uptime.1tmc.top";

    private const string NodeListEndpoint = "/api/node?relay=true&p2pnode=3";
    private const string NodeUrlEndpoint = "/api/node/get/{0}";
    private const int MaxResponseSizeBytes = 1 * 1024 * 1024;

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static string NodeCachePath => Path.Combine(ConfigPath.UserDataRootPath, "Multiplayer", "uptime-nodes.json");

    public static readonly GravityConeNodeClient Instance = new();

    public static bool IsUptimeConfigured =>
        !string.IsNullOrWhiteSpace(ServiceCredentials.GravityConeUptimeApiKey);

    /// <summary>
    ///     获取全部 P2P 发现节点的 -p 连接地址（不含内置/中继节点）。
    ///     获取不到时将抛出异常，由调用方决定是否回退到本地缓存。
    /// </summary>
    public async Task<IReadOnlyList<string>> FetchPeerUrlsAsync(CancellationToken cancellationToken)
    {
        var apiKey = ServiceCredentials.GravityConeUptimeApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                $"未配置 {ServiceCredentials.GravityConeUptimeApiKeyEnvironmentVariable}，无法获取联机节点列表。");

        var p2pNodes = await FetchP2PNodeListAsync(apiKey, cancellationToken);
        if (p2pNodes.Count == 0)
            throw new InvalidOperationException("联机节点服务未返回可用的 P2P 节点。");

        var peers = new List<string>();
        foreach (var node in p2pNodes)
        {
            try
            {
                var url = await FetchNodeUrlAsync(apiKey, node.GetKey, cancellationToken);
                if (!string.IsNullOrWhiteSpace(url) && IsValidPeer(url) && !peers.Contains(url, StringComparer.Ordinal))
                    peers.Add(url);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
            {
                Logger.Warning($"获取联机节点 {node.DisplayName} 地址失败。{Environment.NewLine}{ex}");
            }
        }

        if (peers.Count == 0)
            throw new InvalidOperationException("Uptime 中继节点地址获取失败。");

        await SaveCacheAsync(peers, cancellationToken);
        return peers;
    }

    public async Task<IReadOnlyList<string>?> TryReadCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(NodeCachePath)) return null;
        try
        {
            await using var stream = File.OpenRead(NodeCachePath);
            var cache = await JsonSerializer.DeserializeAsync<NodeCache>(stream, JsonOptions, cancellationToken);
            if (cache is not { SchemaVersion: 1 } || cache.Peers is not { Count: > 0 }) return null;
            return cache.Peers.All(IsValidPeer) ? cache.Peers : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Logger.Warning($"读取联机节点缓存失败。{Environment.NewLine}{ex}");
            return null;
        }
    }

    private static async Task<List<NodeEntry>> FetchP2PNodeListAsync(string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UptimeBaseUrl + NodeListEndpoint);
        ApplyRequestHeaders(request, apiKey);
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await ReadLimitedAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken);
        var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("p2p", out var p2p) ||
            p2p.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("联机节点列表缺少 P2P 节点数据。");

        var nodes = new List<NodeEntry>();
        foreach (var node in p2p.EnumerateArray())
        {
            nodes.Add(new NodeEntry(
                GetInt64(node, "id") ?? 0,
                GetString(node, "name") ?? string.Empty,
                GetString(node, "getKey") ?? string.Empty));
        }

        return nodes;
    }

    /// <summary>
    ///     获取单个节点的连接地址。响应形如 "txt://tcp://..."，去掉前缀后返回。
    /// </summary>
    private static async Task<string> FetchNodeUrlAsync(string apiKey, string getKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            string.Format(UptimeBaseUrl + NodeUrlEndpoint, Uri.EscapeDataString(getKey)));
        ApplyRequestHeaders(request, apiKey);
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await ReadLimitedAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken);
        var raw = body.Trim();
        var url = raw.StartsWith("txt://", StringComparison.Ordinal) ? raw["txt://".Length..] : raw;
        return url.StartsWith("tcp://", StringComparison.Ordinal) ||
               url.StartsWith("udp://", StringComparison.Ordinal) ||
               url.StartsWith("ws://", StringComparison.Ordinal) ||
               url.StartsWith("wss://", StringComparison.Ordinal)
            ? url
            : raw;
    }

    private static void ApplyRequestHeaders(HttpRequestMessage request, string apiKey)
    {
        // Uptime 接口要求固定的 Portal/* User-Agent，不随用户自定义 User-Agent 变化。
        request.Headers.TryAddWithoutValidation("User-Agent", $"Portal/{Data.Instance.Version.VersionTitle}");
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
    }

    private static bool IsValidPeer(string peer) =>
        Uri.TryCreate(peer, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" or "tcp" or "udp" or "ws" or "wss" &&
        !string.IsNullOrWhiteSpace(uri.Host);

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? GetInt64(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind is JsonValueKind.Number && value.TryGetInt64(out var result)
            ? result
            : null;

    private static async Task<string> ReadLimitedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memory.Length + read > MaxResponseSizeBytes)
                throw new InvalidDataException("联机节点响应过大。");
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return System.Text.Encoding.UTF8.GetString(memory.ToArray());
    }

    private static async Task SaveCacheAsync(IReadOnlyList<string> peers, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(NodeCachePath)!);
        var payload = JsonSerializer.Serialize(new { schemaVersion = 1, peers });
        await File.WriteAllTextAsync(NodeCachePath, payload, cancellationToken);
    }

    private sealed record NodeEntry(long Id, string Name, string GetKey)
    {
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id.ToString() : Name;
    }

    private sealed class NodeCache
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
        [JsonPropertyName("peers")] public List<string>? Peers { get; set; }
    }
}
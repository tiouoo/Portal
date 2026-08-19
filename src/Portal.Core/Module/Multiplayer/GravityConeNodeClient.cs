using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Portal.Core.Const;
using Portal.Core.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Module.Multiplayer;

public sealed class GravityConeNodeClient
{
    public const string UptimeBaseUrl = "https://uptime.1tmc.top";

    private const string NodeListEndpoint = "/api/node/?relay=true&p2pnode=3";
    private const string NodeUrlEndpoint = "/api/node/get/{0}";
    private const int MaxResponseSizeBytes = 1 * 1024 * 1024;

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static readonly GravityConeNodeClient Instance = new();

    private static string NodeCachePath =>
        Path.Combine(ConfigPath.UserDataRootPath, "Multiplayer", "uptime-nodes.json");

    public static bool IsUptimeConfigured =>
        !string.IsNullOrWhiteSpace(CredentialsService.GravityConeUptimeApiKey);

    public async Task<IReadOnlyList<string>> FetchPeerUrlsAsync(CancellationToken cancellationToken)
    {
        var apiKey = CredentialsService.GravityConeUptimeApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                string.Format(CommonLanguageManager.Instance.multiplayer_uptimeApiKeyNotConfigured.CurrentValue(), CredentialsService.GravityConeUptimeApiKeyEnvironmentVariable));

        var p2pNodes = await FetchP2PNodeListAsync(apiKey, cancellationToken);
        if (p2pNodes.Count == 0)
            throw new InvalidOperationException(CommonLanguageManager.Instance.multiplayer_noP2pNodes.CurrentValue());

        var urlTasks = p2pNodes.Select(node => FetchNodeUrlSafelyAsync(apiKey, node, cancellationToken)).ToArray();
        var urls = await Task.WhenAll(urlTasks);
        var peers = urls.OfType<string>().Where(url => !string.IsNullOrWhiteSpace(url)).Distinct(StringComparer.Ordinal).ToList();

        if (peers.Count == 0)
            throw new InvalidOperationException(CommonLanguageManager.Instance.multiplayer_uptimeNodeUrlFailed.CurrentValue());

        await SaveCacheAsync(peers, cancellationToken);
        return peers;
    }

    private static async Task<string?> FetchNodeUrlSafelyAsync(string apiKey, NodeEntry node,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = await FetchNodeUrlAsync(apiKey, node.GetKey, cancellationToken);
            return !string.IsNullOrWhiteSpace(url) && IsValidPeer(url) ? url : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
        {
            Logger.Warning(string.Format(LogLanguageManager.Instance.multiplayer_fetchNodeUrlFailed.CurrentValue(), node.DisplayName, Environment.NewLine, ex));
            return null;
        }
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
            Logger.Warning(string.Format(LogLanguageManager.Instance.multiplayer_readNodeCacheFailed.CurrentValue(), Environment.NewLine, ex));
            return null;
        }
    }

    private static async Task<List<NodeEntry>> FetchP2PNodeListAsync(string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UptimeBaseUrl + NodeListEndpoint);
        ApplyRequestHeaders(request, apiKey);
        using var response =
            await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await ReadLimitedAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken);
        var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("p2p", out var p2p) ||
            p2p.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException(CommonLanguageManager.Instance.multiplayer_missingP2pData.CurrentValue());

        var nodes = new List<NodeEntry>();
        foreach (var node in p2p.EnumerateArray())
            nodes.Add(new NodeEntry(
                GetInt64(node, "id") ?? 0,
                GetString(node, "name") ?? string.Empty,
                GetString(node, "getKey") ?? string.Empty));

        return nodes;
    }

    private static async Task<string> FetchNodeUrlAsync(string apiKey, string getKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            string.Format(UptimeBaseUrl + NodeUrlEndpoint, Uri.EscapeDataString(getKey)));
        ApplyRequestHeaders(request, apiKey);
        using var response =
            await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await ReadLimitedAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken);
        var raw = body.Trim();
        if (raw.StartsWith("txt://", StringComparison.Ordinal))
        {
            var inner = raw["txt://".Length..];
            if (inner.StartsWith("tcp://", StringComparison.Ordinal) ||
                inner.StartsWith("udp://", StringComparison.Ordinal) ||
                inner.StartsWith("ws://", StringComparison.Ordinal) ||
                inner.StartsWith("wss://", StringComparison.Ordinal))
                return inner;
            return raw;
        }

        return raw;
    }

    private static void ApplyRequestHeaders(HttpRequestMessage request, string apiKey)
    {
        request.Headers.TryAddWithoutValidation("User-Agent",
            $"Portal/{AppVersionService.Instance.Version.VersionTitle}");
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
    }

    internal static bool IsValidPeer(string peer)
    {
        return Uri.TryCreate(peer, UriKind.Absolute, out var uri) &&
               uri.Scheme is "http" or "https" or "tcp" or "udp" or "ws" or "wss" or "txt" &&
               !string.IsNullOrWhiteSpace(uri.Host);
    }

    private static string? GetString(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long? GetInt64(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
               value.ValueKind is JsonValueKind.Number && value.TryGetInt64(out var result)
            ? result
            : null;
    }

    private static async Task<string> ReadLimitedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memory.Length + read > MaxResponseSizeBytes)
                throw new InvalidDataException(CommonLanguageManager.Instance.multiplayer_responseTooLarge.CurrentValue());
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return Encoding.UTF8.GetString(memory.ToArray());
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
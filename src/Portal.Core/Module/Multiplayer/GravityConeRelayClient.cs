using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Portal.Core.Const;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Module.Multiplayer;

public sealed class GravityConeRelayClient
{
    public const string RelayListUrl = "https://portal.tiouo.cc/relays.json";

    private const int MaxResponseSizeBytes = 1 * 1024 * 1024;

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static readonly GravityConeRelayClient Instance = new();
    private static string CachePath => Path.Combine(ConfigPath.UserDataRootPath, "Multiplayer", "portal-relays.json");

    public async Task PrefetchAsync(CancellationToken cancellationToken = default)
    {
        var relays = await FetchRelaysAsync(cancellationToken);
        Logger.Info(string.Format(LogLanguageManager.Instance.multiplayer_relaysPrefetched.CurrentValue(), relays.Count));
    }

    public async Task<IReadOnlyList<string>> FetchRelaysAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, RelayListUrl);
        using var response =
            await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await ReadLimitedAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken);
        var document = JsonDocument.Parse(body);
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

        result = result.Distinct(StringComparer.Ordinal).ToList();
        if (result.Count == 0)
            throw new InvalidDataException(CommonLanguageManager.Instance.multiplayer_relaysNoUsableNodes.CurrentValue());

        await SaveCacheAsync(result, cancellationToken);
        return result;
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
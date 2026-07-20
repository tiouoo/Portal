using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using fNbt;
using Portal.Core.Minecraft.Classes;

namespace Portal.Core.Minecraft.Services;

public sealed class RecentPlayService
{
    private const string HistoryFileName = "Portal.recent-play.json";
    private readonly WorldSaveService _worldSaveService = new();

    public async Task<IReadOnlyList<RecentPlayTarget>> ScanAsync(IEnumerable<MinecraftInstance> instances,
        CancellationToken cancellationToken = default)
    {
        var targets = new List<RecentPlayTarget>();
        foreach (var instance in instances.Where(instance => instance.Type == MinecraftInstanceType.Java))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var history = ReadHistory(instance);
            var servers = ReadServers(instance, out _).ToArray();
            MergeConnectionLogs(instance, history, servers);
            var worlds = await _worldSaveService.ScanAsync(instance, cancellationToken);
            targets.AddRange(worlds
                .Where(world => world.LastPlayedTime.HasValue)
                .Select(world => new RecentPlayTarget(instance, RecentPlayTargetType.World, world.FolderName,
                    string.IsNullOrWhiteSpace(world.LevelName) ? world.FolderName : world.LevelName,
                    $"存档·{world.Version ?? "未知版本"}·{GetGameModeText(world.GameMode)}",
                    world.LastPlayedTime!.Value, world.IconPath)));

            foreach (var server in servers)
            {
                var recorded = history.FirstOrDefault(item => IsSameServer(item, server.Host, server.Port));
                if (recorded == null)
                    continue;

                targets.Add(new RecentPlayTarget(instance, RecentPlayTargetType.Server,
                    GetServerHistoryKey(server.Address, server.Port), server.Name, $"服务器·{server.Address}",
                    recorded.LastPlayedTime, ServerIconData: server.IconData, ServerAddress: server.Host, ServerPort: server.Port));
            }

            // Direct connections and LAN worlds are not guaranteed to appear in servers.dat.
            // Entries recorded as saved are intentionally omitted when their server was later removed.
            foreach (var recorded in history.Where(item => !item.WasSaved &&
                         !servers.Any(server => IsSameServer(item, server.Host, server.Port))))
            {
                targets.Add(new RecentPlayTarget(instance, RecentPlayTargetType.Server,
                    GetServerHistoryKey(recorded.Address, recorded.Port), recorded.Name ?? recorded.Address,
                    $"{(IsLanAddress(recorded.Address) ? "局域网" : "服务器")}·{recorded.Address}:{recorded.Port}",
                    recorded.LastPlayedTime, ServerAddress: recorded.Address, ServerPort: recorded.Port));
            }
        }

        return targets.OrderByDescending(target => target.LastPlayedTime).ToArray();
    }

    public void RecordServerPlay(MinecraftInstance instance, string address, int port)
    {
        var history = ReadHistory(instance);
        var servers = ReadServers(instance, out _).ToArray();
        var savedServer = servers.FirstOrDefault(server => IsSameServer(address, port, server.Host, server.Port));
        history.RemoveAll(item => IsSameServer(item, address, port));
        history.Add(new RecentServerHistory(address, port, savedServer?.Name, savedServer != null, DateTime.Now));
        WriteHistory(instance, history);
    }

    public void RecordServerConnection(MinecraftInstance instance, string logLine)
    {
        if (!TryGetConnection(logLine, out var address, out var port))
            return;

        RecordServerPlay(instance, address, port);
    }

    private static void MergeConnectionLogs(MinecraftInstance instance, List<RecentServerHistory> history,
        IReadOnlyCollection<ServerEntry> servers)
    {
        var logsPath = instance.GetSpecialFolder(MinecraftSpecialFolder.LogsFolder);
        if (!Directory.Exists(logsPath))
            return;

        IEnumerable<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(logsPath, "*.log").Append(Path.Combine(logsPath, "latest.log"))
                .Concat(Directory.EnumerateFiles(logsPath, "*.log.gz")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }

        foreach (var path in paths.Where(File.Exists))
        {
            try
            {
                using var file = File.OpenRead(path);
                Stream stream = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                    ? new GZipStream(file, CompressionMode.Decompress)
                    : file;
                using (stream)
                {
                using var reader = new StreamReader(stream);
                var lastWriteTime = File.GetLastWriteTime(path);
                while (reader.ReadLine() is { } line)
                {
                    if (!TryGetConnection(line, out var address, out var port))
                        continue;

                    var savedServer = servers.FirstOrDefault(server => IsSameServer(address, port, server.Host, server.Port));
                    var timestamp = GetLogTimestamp(line, lastWriteTime);
                    var index = history.FindIndex(item => IsSameServer(item, address, port));
                    var entry = new RecentServerHistory(address, port, savedServer?.Name, savedServer != null, timestamp);
                    if (index < 0)
                        history.Add(entry);
                    else if (history[index].LastPlayedTime < timestamp)
                        history[index] = entry;
                }
                }
            }
            catch (IOException) { }
            catch (InvalidDataException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static IEnumerable<ServerEntry> ReadServers(MinecraftInstance instance, out DateTime lastWriteTime)
    {
        var path = Path.Combine(instance.MinecraftEntry!.MinecraftFolderPath, "servers.dat");
        lastWriteTime = DateTime.MinValue;
        if (!File.Exists(path))
            return [];

        lastWriteTime = File.GetLastWriteTime(path);

        try
        {
            var file = new NbtFile();
            file.LoadFromFile(path);
            return (file.RootTag["servers"] as NbtList)?.OfType<NbtCompound>()
                .Select(server => CreateServerEntry(server))
                .Where(server => server != null)
                .Cast<ServerEntry>()
                .ToArray() ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static ServerEntry? CreateServerEntry(NbtCompound server)
    {
        var address = (server["ip"] as NbtString)?.Value;
        if (string.IsNullOrWhiteSpace(address))
            return null;

        var (host, port) = ParseAddress(address);
        var iconText = (server["icon"] as NbtString)?.Value;
        byte[]? icon = null;
        if (!string.IsNullOrWhiteSpace(iconText))
        {
            var encoded = iconText[(iconText.IndexOf(',') + 1)..];
            try { icon = Convert.FromBase64String(encoded); }
            catch (FormatException) { }
        }

        return new ServerEntry((server["name"] as NbtString)?.Value ?? host, address, host, port, icon);
    }

    private static (string Host, int Port) ParseAddress(string address)
    {
        var separator = address.LastIndexOf(':');
        return separator > 0 && int.TryParse(address[(separator + 1)..], out var port)
            ? (address[..separator], port)
            : (address, 25565);
    }

    private static readonly Regex ConnectingPattern = new(@"\bConnecting to ([^,\s]+),\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex LogTimePattern = new(@"^\[(\d{2}:\d{2}:\d{2})\]", RegexOptions.Compiled);

    private static bool TryGetConnection(string logLine, out string address, out int port)
    {
        var match = ConnectingPattern.Match(logLine);
        address = match.Success ? match.Groups[1].Value : string.Empty;
        port = 0;
        return match.Success && int.TryParse(match.Groups[2].Value, out port);
    }

    private static DateTime GetLogTimestamp(string line, DateTime fallback)
    {
        var match = LogTimePattern.Match(line);
        if (!match.Success || !TimeOnly.TryParse(match.Groups[1].Value, out var time))
            return fallback;

        var timestamp = fallback.Date.Add(time.ToTimeSpan());
        return timestamp > fallback.AddMinutes(1) ? timestamp.AddDays(-1) : timestamp;
    }

    private static string GetServerHistoryKey(string address, int port) => $"server:{address}:{port}";

    private static bool IsSameServer(RecentServerHistory history, string address, int port) =>
        IsSameServer(history.Address, history.Port, address, port);

    private static bool IsSameServer(string leftAddress, int leftPort, string rightAddress, int rightPort) =>
        leftPort == rightPort && string.Equals(leftAddress, rightAddress, StringComparison.OrdinalIgnoreCase);

    private static bool IsLanAddress(string address) =>
        address.StartsWith("192.168.", StringComparison.Ordinal) ||
        address.StartsWith("10.", StringComparison.Ordinal) ||
        address.StartsWith("172.16.", StringComparison.Ordinal) ||
        address.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        address.Equals("127.0.0.1", StringComparison.Ordinal);

    private static List<RecentServerHistory> ReadHistory(MinecraftInstance instance)
    {
        var path = Path.Combine(instance.MinecraftPath, HistoryFileName);
        try
        {
            if (!File.Exists(path))
                return [];

            var json = File.ReadAllText(path);
            if (json.TrimStart().StartsWith('['))
                return JsonSerializer.Deserialize<List<RecentServerHistory>>(json) ?? [];

            var legacyHistory = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json) ?? [];
            return legacyHistory.Select(item => CreateLegacyHistory(item.Key, item.Value)).Where(item => item != null)
                .Cast<RecentServerHistory>().ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static RecentServerHistory? CreateLegacyHistory(string key, DateTime lastPlayedTime)
    {
        const string prefix = "server:";
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var (address, port) = ParseAddress(key[prefix.Length..]);
        return new RecentServerHistory(address, port, null, true, lastPlayedTime);
    }

    private static void WriteHistory(MinecraftInstance instance, List<RecentServerHistory> history)
    {
        var path = Path.Combine(instance.MinecraftPath, HistoryFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(history));
    }

    private static string GetGameModeText(int? gameMode) => gameMode switch
    {
        0 => "生存", 1 => "创造", 2 => "冒险", 3 => "旁观", _ => "未知模式"
    };

    private sealed record ServerEntry(string Name, string Address, string Host, int Port, byte[]? IconData);
    private sealed record RecentServerHistory(string Address, int Port, string? Name, bool WasSaved, DateTime LastPlayedTime);
}

using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using fNbt;
using Portal.Core.Minecraft.Classes;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Services;

public sealed class RecentPlayService
{
    private const string HistoryFileName = "Portal.recent-play.json";
    // 记录回调可能来自游戏进程输出线程，需要串行化历史文件的读改写
    private static readonly object HistoryLock = new();
    private readonly WorldSaveService _worldSaveService = new();

    public Task<IReadOnlyList<RecentPlayTarget>> ScanAsync(IEnumerable<MinecraftInstance> instances,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(instances.ToArray(), cancellationToken), cancellationToken);

    private IReadOnlyList<RecentPlayTarget> Scan(IReadOnlyList<MinecraftInstance> instances,
        CancellationToken cancellationToken)
    {
        var targets = new List<RecentPlayTarget>();
        foreach (var instance in instances.Where(instance => instance.Type == MinecraftInstanceType.Java))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var history = ReadHistory(instance);
            var servers = ReadServers(instance, out _).ToArray();
            MergeConnectionLogs(instance, history, servers);
            var worlds = _worldSaveService.ScanAsync(instance, cancellationToken).GetAwaiter().GetResult();
            targets.AddRange(worlds
                .Where(world => world.LastPlayedTime.HasValue)
                .Select(world => new RecentPlayTarget(instance, RecentPlayTargetType.World, world.FolderName,
                    string.IsNullOrWhiteSpace(world.LevelName) ? world.FolderName : world.LevelName,
                    $"存档·{world.Version ?? "未知版本"}·{GetGameModeText(world.GameMode)}",
                    world.LastPlayedTime!.Value, world.IconPath)));

            foreach (var server in servers.Where(server => !IsLanAddress(server.Host)))
            {
                var recorded = history.FirstOrDefault(item => IsSameServer(item, server.Host, server.Port));
                if (recorded == null)
                    continue;

                targets.Add(new RecentPlayTarget(instance, RecentPlayTargetType.Server,
                    GetServerHistoryKey(server.Address, server.Port), server.Name, $"服务器·{server.Address}",
                    recorded.LastPlayedTime, ServerIconData: server.IconData, ServerAddress: server.Host, ServerPort: server.Port));
            }

            // Direct connections are not guaranteed to appear in servers.dat. LAN addresses are deliberately
            // excluded so recent play contains only saved worlds and external servers.
            // Entries recorded as saved are intentionally omitted when their server was later removed.
            foreach (var recorded in history.Where(item => !item.WasSaved && !IsLanAddress(item.Address) &&
                          !servers.Any(server => IsSameServer(item, server.Host, server.Port))))
            {
                targets.Add(new RecentPlayTarget(instance, RecentPlayTargetType.Server,
                    GetServerHistoryKey(recorded.Address, recorded.Port), recorded.Name ?? recorded.Address,
                    $"服务器·{recorded.Address}:{recorded.Port}",
                    recorded.LastPlayedTime, ServerAddress: recorded.Address, ServerPort: recorded.Port));
            }
        }

        return targets.OrderByDescending(target => target.LastPlayedTime).ToArray();
    }

    public void RecordServerPlay(MinecraftInstance instance, string address, int port)
    {
        try
        {
            lock (HistoryLock)
            {
                var history = ReadHistory(instance);
                var servers = ReadServers(instance, out _).ToArray();
                var savedServer = servers.FirstOrDefault(server => IsSameServer(address, port, server.Host, server.Port));
                history.RemoveAll(item => IsSameServer(item, address, port));
                history.Add(new RecentServerHistory(address, port, savedServer?.Name, savedServer != null, DateTime.Now));
                WriteHistory(instance, history);
            }
        }
        catch (Exception e)
        {
            // 该方法在进程输出事件线程上执行，异常不能外抛，否则会导致进程崩溃
            Logger.Error("记录服务器游玩历史失败。", e);
        }
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
        catch (IOException exception)
        {
            Logger.Warning($"枚举 Minecraft 日志目录失败：{logsPath}{Environment.NewLine}{exception}");
            return;
        }
        catch (UnauthorizedAccessException exception)
        {
            Logger.Warning($"没有权限枚举 Minecraft 日志目录：{logsPath}{Environment.NewLine}{exception}");
            return;
        }

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
            catch (IOException exception)
            {
                Logger.Warning($"读取 Minecraft 日志以分析最近服务器失败：{path}{Environment.NewLine}{exception}");
            }
            catch (InvalidDataException exception)
            {
                Logger.Warning($"解析 Minecraft 日志以分析最近服务器失败：{path}{Environment.NewLine}{exception}");
            }
            catch (UnauthorizedAccessException exception)
            {
                Logger.Warning($"没有权限读取 Minecraft 日志：{path}{Environment.NewLine}{exception}");
            }
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
        catch (Exception exception)
        {
            Logger.Warning($"读取服务器列表失败：{path}{Environment.NewLine}{exception}");
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
            catch (FormatException exception)
            {
                Logger.Warning($"服务器图标 Base64 数据无效，将忽略图标。{Environment.NewLine}{exception}");
            }
        }

        return new ServerEntry((server["name"] as NbtString)?.Value ?? host, address, host, port, icon);
    }

    private static (string Host, int Port) ParseAddress(string address)
    {
        // [addr]:port 形式的 IPv6 地址
        if (address.StartsWith('['))
        {
            var end = address.IndexOf(']');
            if (end > 0)
            {
                var host = address[1..end];
                return address.Length > end + 1 && address[end + 1] == ':' &&
                       int.TryParse(address[(end + 2)..], out var bracketPort)
                    ? (host, bracketPort)
                    : (host, 25565);
            }
        }

        // 含多个 ':' 的裸 IPv6 地址整体视为主机
        var separator = address.LastIndexOf(':');
        return separator > 0 && address.IndexOf(':') == separator &&
               int.TryParse(address[(separator + 1)..], out var port)
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
        Is172PrivateAddress(address) ||
        address.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        address.Equals("127.0.0.1", StringComparison.Ordinal);

    // 172.16.0.0/12 私有网段覆盖 172.16.* 至 172.31.*
    private static bool Is172PrivateAddress(string address)
    {
        if (!address.StartsWith("172.", StringComparison.Ordinal))
            return false;

        var end = address.IndexOf('.', 4);
        return end > 4 && int.TryParse(address[4..end], out var second) && second is >= 16 and <= 31;
    }

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

        var addressAndPort = key[prefix.Length..];
        // 旧键以 address:port 拼接，裸 IPv6 必须从最后一个分隔符拆分端口。
        var separator = addressAndPort.LastIndexOf(':');
        var (address, port) = separator > 0 && int.TryParse(addressAndPort[(separator + 1)..], out var legacyPort)
            ? (addressAndPort[..separator], legacyPort)
            : ParseAddress(addressAndPort);
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

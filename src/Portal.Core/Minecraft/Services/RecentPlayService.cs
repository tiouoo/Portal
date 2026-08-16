using System.Text.Json;
using System.Text.RegularExpressions;
using Portal.Core.Minecraft.Classes;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Services;

public sealed class RecentPlayService
{
    private const string HistoryFileName = "Portal.recent-play.json";
    
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
            var servers = JavaServerManager.Read(instance).ToArray();
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
                var servers = JavaServerManager.Read(instance).ToArray();
                var savedServer = servers.FirstOrDefault(server => IsSameServer(address, port, server.Host, server.Port));
                history.RemoveAll(item => IsSameServer(item, address, port));
                history.Add(new RecentServerHistory(address, port, savedServer?.Name, savedServer != null, DateTime.Now));
                WriteHistory(instance, history);
            }
        }
        catch (Exception e)
        {
            
            Logger.Error("记录服务器游玩历史失败。", e);
        }
    }

    public void RecordServerConnection(MinecraftInstance instance, string logLine)
    {
        if (!TryGetConnection(logLine, out var address, out var port))
            return;

        RecordServerPlay(instance, address, port);
    }

    private static readonly Regex ConnectingPattern = new(@"\bConnecting to ([^,\s]+),\s*(\d+)", RegexOptions.Compiled);

    private static bool TryGetConnection(string logLine, out string address, out int port)
    {
        var match = ConnectingPattern.Match(logLine);
        address = match.Success ? match.Groups[1].Value : string.Empty;
        port = 0;
        return match.Success && int.TryParse(match.Groups[2].Value, out port);
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
        
        var separator = addressAndPort.LastIndexOf(':');
        var (address, port) = separator > 0 && int.TryParse(addressAndPort[(separator + 1)..], out var legacyPort)
            ? (addressAndPort[..separator], legacyPort)
            : JavaServerManager.ParseAddress(addressAndPort);
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

    private sealed record RecentServerHistory(string Address, int Port, string? Name, bool WasSaved, DateTime LastPlayedTime);
}

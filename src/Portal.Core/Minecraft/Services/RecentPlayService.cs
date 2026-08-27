using System.Text.Json;
using System.Text.RegularExpressions;
using Portal.Core.Minecraft.Classes;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Services;

public sealed class RecentPlayService
{
    private const string HistoryFileName = "Portal.recent-play.json";
    private const int MaxScanConcurrency = 4;

    private static readonly object HistoryLock = new();

    private static readonly Regex ConnectingPattern = new(@"\bConnecting to ([^,\s]+),\s*(\d+)", RegexOptions.Compiled);
    private readonly WorldSaveService _worldSaveService = new();

    public Task<IReadOnlyList<RecentPlayTarget>> ScanAsync(IEnumerable<MinecraftInstance> instances,
        CancellationToken cancellationToken = default)
    {
        var javaInstances = instances.Where(instance => instance.Type == MinecraftInstanceType.Java).ToArray();
        return Task.Run(() => ScanAsyncCore(javaInstances, cancellationToken), cancellationToken);
    }

    private async Task<IReadOnlyList<RecentPlayTarget>> ScanAsyncCore(IReadOnlyList<MinecraftInstance> instances,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(MaxScanConcurrency);
        var scans = instances.Select(async instance =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await ScanInstanceAsync(instance, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        return (await Task.WhenAll(scans).ConfigureAwait(false))
            .SelectMany(items => items)
            .OrderByDescending(target => target.LastPlayedTime)
            .ToArray();
    }

    private async Task<IReadOnlyList<RecentPlayTarget>> ScanInstanceAsync(MinecraftInstance instance,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var history = ReadHistory(instance);
        var servers = JavaServerManager.Read(instance).ToArray();
        var worlds = await _worldSaveService.ScanAsync(instance, cancellationToken).ConfigureAwait(false);
        var targets = worlds
            .Where(world => world.LastPlayedTime.HasValue)
            .Select(world => new RecentPlayTarget(instance, RecentPlayTargetType.World, world.FolderName,
                string.IsNullOrWhiteSpace(world.LevelName) ? world.FolderName : world.LevelName,
                string.Format(CommonLanguageManager.Instance.recentPlay_saveDescription.CurrentValue(),
                    world.Version ?? CommonLanguageManager.Instance.recentPlay_unknownVersion.CurrentValue(),
                    GetGameModeText(world.GameMode)),
                world.LastPlayedTime!.Value, world.IconPath))
            .ToList();

        foreach (var server in servers.Where(server => !IsLanAddress(server.Host)))
        {
            var recorded = history.FirstOrDefault(item => IsSameServer(item, server.Host, server.Port));
            if (recorded == null)
                continue;

            targets.Add(new RecentPlayTarget(instance, RecentPlayTargetType.Server,
                GetServerHistoryKey(server.Address, server.Port), server.Name,
                string.Format(CommonLanguageManager.Instance.recentPlay_serverDescription.CurrentValue(), server.Address),
                recorded.LastPlayedTime, ServerIconData: server.IconData, ServerAddress: server.Host,
                ServerPort: server.Port));
        }

        foreach (var recorded in history.Where(item => !item.WasSaved && !IsLanAddress(item.Address) &&
                                                       !servers.Any(server =>
                                                           IsSameServer(item, server.Host, server.Port))))
            targets.Add(new RecentPlayTarget(instance, RecentPlayTargetType.Server,
                GetServerHistoryKey(recorded.Address, recorded.Port), recorded.Name ?? recorded.Address,
                string.Format(CommonLanguageManager.Instance.recentPlay_serverDescriptionWithPort.CurrentValue(), recorded.Address, recorded.Port),
                recorded.LastPlayedTime, ServerAddress: recorded.Address, ServerPort: recorded.Port));

        return targets;
    }

    public void RecordServerPlay(MinecraftInstance instance, string address, int port)
    {
        try
        {
            lock (HistoryLock)
            {
                var history = ReadHistory(instance);
                var servers = JavaServerManager.Read(instance).ToArray();
                var savedServer =
                    servers.FirstOrDefault(server => IsSameServer(address, port, server.Host, server.Port));
                history.RemoveAll(item => IsSameServer(item, address, port));
                history.Add(
                    new RecentServerHistory(address, port, savedServer?.Name, savedServer != null, DateTime.Now));
                WriteHistory(instance, history);
            }
        }
        catch (Exception e)
        {
            Logger.Error(LogLanguageManager.Instance.recentPlay_recordFailed.CurrentValue(), e);
        }
    }

    public void RecordServerConnection(MinecraftInstance instance, string logLine)
    {
        if (!TryGetConnection(logLine, out var address, out var port))
            return;

        RecordServerPlay(instance, address, port);
    }

    private static bool TryGetConnection(string logLine, out string address, out int port)
    {
        var match = ConnectingPattern.Match(logLine);
        address = match.Success ? match.Groups[1].Value : string.Empty;
        port = 0;
        return match.Success && int.TryParse(match.Groups[2].Value, out port);
    }

    private static string GetServerHistoryKey(string address, int port)
    {
        return $"server:{address}:{port}";
    }

    private static bool IsSameServer(RecentServerHistory history, string address, int port)
    {
        return IsSameServer(history.Address, history.Port, address, port);
    }

    private static bool IsSameServer(string leftAddress, int leftPort, string rightAddress, int rightPort)
    {
        return leftPort == rightPort && string.Equals(leftAddress, rightAddress, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLanAddress(string address)
    {
        return address.StartsWith("192.168.", StringComparison.Ordinal) ||
               address.StartsWith("10.", StringComparison.Ordinal) ||
               Is172PrivateAddress(address) ||
               address.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               address.Equals("127.0.0.1", StringComparison.Ordinal);
    }


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

    private static string GetGameModeText(int? gameMode)
    {
        return gameMode switch
        {
            0 => CommonLanguageManager.Instance.recentPlay_gameModeSurvival.CurrentValue(),
            1 => CommonLanguageManager.Instance.recentPlay_gameModeCreative.CurrentValue(),
            2 => CommonLanguageManager.Instance.recentPlay_gameModeAdventure.CurrentValue(),
            3 => CommonLanguageManager.Instance.recentPlay_gameModeSpectator.CurrentValue(),
            _ => CommonLanguageManager.Instance.recentPlay_gameModeUnknown.CurrentValue()
        };
    }

    private sealed record RecentServerHistory(
        string Address,
        int Port,
        string? Name,
        bool WasSaved,
        DateTime LastPlayedTime);
}

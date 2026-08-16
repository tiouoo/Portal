using System.Text;
using Portal.Bedrock.Standard.Manifest;
using Portal.Core.Minecraft.Instance.Bedrock;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Services;

public static class BedrockServerManager
{
    public const int DefaultPort = 19132;
    private static readonly object FileLock = new();

    public static string GetExternalServersPath(BedrockInstanceConfig config, string userId = "Shared") =>
        Path.Combine(BedrockDataPathResolver.GetMojangDataRoot(config, userId), "minecraftpe", "external_servers.txt");

        public static IReadOnlyList<BedrockServerEntry> Read(BedrockInstanceConfig config, string userId)
    {
        lock (FileLock)
        {
            var path = GetExternalServersPath(config, userId);
            if (!File.Exists(path))
                return [];

            var entries = new List<BedrockServerEntry>();
            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var entry = ParseLine(lines[index]);
                if (entry == null)
                    continue;

                entry.LineIndex = index;
                entries.Add(entry);
            }

            return entries;
        }
    }

        public static bool Add(BedrockInstanceConfig config, string userId, string name, string address)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        lock (FileLock)
        {
            try
            {
                var path = GetExternalServersPath(config, userId);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
                var (host, port) = ParseAddress(address.Trim());
                lines.Add(BuildLine(GetNextIndex(lines), name.Trim(), host, port,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
                WriteLines(path, lines);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Warning($"添加基岩版服务器失败：{name} {address}{Environment.NewLine}{exception}");
                return false;
            }
        }
    }

        public static bool Update(BedrockInstanceConfig config, string userId, int lineIndex, string name, string address)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        lock (FileLock)
        {
            try
            {
                var path = GetExternalServersPath(config, userId);
                if (!File.Exists(path))
                    return false;

                var lines = File.ReadAllLines(path).ToList();
                if (lineIndex < 0 || lineIndex >= lines.Count)
                    return false;

                var existing = ParseLine(lines[lineIndex]);
                if (existing == null)
                    return false;

                var (host, port) = ParseAddress(address.Trim());
                lines[lineIndex] = existing.IsNewFormat
                    ? BuildLine(existing.Index, name.Trim(), host, port, existing.Timestamp)
                    : BuildLegacyLine(name.Trim(), address.Trim(), existing.IconText, existing.Hidden);
                WriteLines(path, lines);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Warning($"编辑基岩版服务器失败：{name} {address}{Environment.NewLine}{exception}");
                return false;
            }
        }
    }

        public static bool Remove(BedrockInstanceConfig config, string userId, int lineIndex)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        lock (FileLock)
        {
            try
            {
                var path = GetExternalServersPath(config, userId);
                if (!File.Exists(path))
                    return false;

                var lines = File.ReadAllLines(path).ToList();
                if (lineIndex < 0 || lineIndex >= lines.Count)
                    return false;

                lines.RemoveAt(lineIndex);
                WriteLines(path, lines);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Warning($"删除基岩版服务器失败：{lineIndex}{Environment.NewLine}{exception}");
                return false;
            }
        }
    }

        public static (string Host, int Port) ParseAddress(string address)
    {
        if (address.StartsWith('['))
        {
            var end = address.IndexOf(']');
            if (end > 0)
            {
                var host = address[1..end];
                return address.Length > end + 1 && address[end + 1] == ':' &&
                       int.TryParse(address[(end + 2)..], out var bracketPort)
                    ? (host, bracketPort)
                    : (host, DefaultPort);
            }
        }

        var separator = address.LastIndexOf(':');
        return separator > 0 && address.IndexOf(':') == separator &&
               int.TryParse(address[(separator + 1)..], out var port)
            ? (address[..separator], port)
            : (address, DefaultPort);
    }

    private static BedrockServerEntry? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        
        return line.Contains(';') ? ParseLegacyLine(line) : ParseColonLine(line);
    }

        private static BedrockServerEntry? ParseColonLine(string line)
    {
        var parts = line.Split(':');
        if (parts.Length < 4)
            return null;

        var index = int.TryParse(parts[0].Trim(), out var parsedIndex) ? parsedIndex : -1;
        var name = parts[1].Trim();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var (host, hostPort) = ParseAddress(parts[2].Trim());
        if (string.IsNullOrWhiteSpace(host))
            return null;

        var port = int.TryParse(parts[3].Trim(), out var fieldPort) ? fieldPort : hostPort;
        if (port is < 1 or > 65535)
            port = hostPort;

        var timestamp = parts.Length > 4 && long.TryParse(parts[4].Trim(), out var parsedTimestamp)
            ? parsedTimestamp
            : 0;

        return new BedrockServerEntry
        {
            Index = index,
            Name = name,
            Address = port == DefaultPort ? host : $"{host}:{port}",
            Host = host,
            Port = port,
            Timestamp = timestamp,
            IsNewFormat = true
        };
    }

        private static BedrockServerEntry? ParseLegacyLine(string line)
    {
        var parts = line.Split(';');
        if (parts.Length < 2)
            return null;

        var name = parts[0].Trim();
        var address = parts[1].Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
            return null;

        var (host, port) = ParseAddress(address);
        var iconText = parts.Length > 2 ? parts[2] : string.Empty;
        var hidden = parts.Length > 3 && parts[3].Trim() == "1";

        return new BedrockServerEntry
        {
            Name = name,
            Address = address,
            Host = host,
            Port = port,
            IconText = iconText,
            IconData = DecodeIcon(iconText),
            Hidden = hidden
        };
    }

    private static byte[]? DecodeIcon(string iconText)
    {
        if (string.IsNullOrWhiteSpace(iconText))
            return null;

        var comma = iconText.IndexOf(',');
        var encoded = comma >= 0 ? iconText[(comma + 1)..] : iconText;
        try
        {
            return Convert.FromBase64String(encoded);
        }
        catch (FormatException exception)
        {
            Logger.Warning($"基岩版服务器图标 Base64 数据无效，将忽略图标。{Environment.NewLine}{exception}");
            return null;
        }
    }

    private static string BuildLine(int index, string name, string host, int port, long timestamp) =>
        $"{index}:{name}:{host}:{port}:{timestamp}";

    private static string BuildLegacyLine(string name, string address, string iconText, bool hidden) =>
        $"{name};{address};{iconText};{(hidden ? 1 : 0)}";

        private static int GetNextIndex(IReadOnlyList<string> lines)
    {
        var maxIndex = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Contains(';'))
                continue;

            var first = line.Split(':')[0].Trim();
            if (int.TryParse(first, out var index) && index > maxIndex)
                maxIndex = index;
        }

        return maxIndex + 1;
    }

    private static void WriteLines(string path, IReadOnlyList<string> lines) =>
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
}

public sealed class BedrockServerEntry
{
    public int LineIndex { get; set; }

        public int Index { get; init; } = -1;

    public required string Name { get; set; }
    public required string Address { get; set; }
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 19132;

        public long Timestamp { get; init; }

        public bool IsNewFormat { get; init; }

    public string IconText { get; init; } = string.Empty;
    public byte[]? IconData { get; init; }
    public bool Hidden { get; init; }

        public string DisplayAddress
    {
        get
        {
            var address = Port == 19132 ? Host : $"{Host}:{Port}";
            if (Timestamp <= 0) return address;

            var formattedTime = DateTimeOffset.FromUnixTimeSeconds(Timestamp).UtcDateTime
                .ToString("yyyy-MM-dd HH:mm:ss");

            return $"{address}·{formattedTime}";
        }
    }
        public string CopyAddress => Port == 19132 ? Host : $"{Host}:{Port}";
}

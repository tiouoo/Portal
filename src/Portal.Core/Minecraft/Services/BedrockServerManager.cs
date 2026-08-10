using System.Text;
using Portal.Bedrock.Standard.Manifest;
using Portal.Core.Minecraft.Instance.Bedrock;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Services;

/// <summary>
/// 基岩版服务器列表（external_servers.txt）管理服务。
/// 文件按用户 ID 区分存储：&lt;com.mojang&gt;/minecraftpe/external_servers.txt。
/// 新版（Minecraft 1.21.120+ / 26.x）使用冒号分隔记录：index:name:host:port:lastPlayed；
/// 旧版使用分号分隔记录：name;address;icon;hidden。读取时两种格式均兼容。
/// </summary>
public static class BedrockServerManager
{
    public const int DefaultPort = 19132;
    private static readonly object FileLock = new();

    public static string GetExternalServersPath(BedrockInstanceConfig config, string userId = "Shared") =>
        Path.Combine(BedrockDataPathResolver.GetMojangDataRoot(config, userId), "minecraftpe", "external_servers.txt");

    /// <summary>
    /// 读取指定用户 ID 的基岩版服务器列表。
    /// </summary>
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

    /// <summary>
    /// 向指定用户 ID 的服务器列表添加服务器（按新版冒号格式写入）。
    /// </summary>
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

    /// <summary>
    /// 编辑指定行号的基岩版服务器（保留图标等其余字段）。
    /// </summary>
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

    /// <summary>
    /// 删除指定行号的基岩版服务器。
    /// </summary>
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

    /// <summary>
    /// 解析服务器地址，支持 host、host:port 与 [ipv6]:port 形式，默认端口 19132。
    /// </summary>
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

        // 新版为冒号分隔，旧版地址（可能含 host:port）与图标字段均以分号分隔，故用分号区分两种格式
        return line.Contains(';') ? ParseLegacyLine(line) : ParseColonLine(line);
    }

    /// <summary>
    /// 解析新版冒号格式：index:name:host:port:lastPlayed。
    /// </summary>
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

    /// <summary>
    /// 解析旧版分号格式：name;address;icon;hidden。
    /// </summary>
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

    /// <summary>
    /// 计算下一序号：取现有新版格式记录中最大的序号 + 1，无记录时从 1 开始。
    /// </summary>
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

/// <summary>
/// external_servers.txt 中的一条服务器记录。
/// </summary>
public sealed class BedrockServerEntry
{
    public int LineIndex { get; set; }

    /// <summary>新版格式中的序号（index），旧版格式为 -1。</summary>
    public int Index { get; init; } = -1;

    public required string Name { get; set; }
    public required string Address { get; set; }
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 19132;

    /// <summary>添加服务器的时间戳（Unix 秒），旧版格式或无记录时为 0。</summary>
    public long Timestamp { get; init; }

    /// <summary>是否为新版冒号格式记录。</summary>
    public bool IsNewFormat { get; init; }

    public string IconText { get; init; } = string.Empty;
    public byte[]? IconData { get; init; }
    public bool Hidden { get; init; }

    /// <summary>页面展示的地址：Host·时间戳，时间戳为 0 时退回 Host[:Port]。</summary>
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
    /// <summary>用于复制/检测的纯地址（不含时间戳）。</summary>
    public string CopyAddress => Port == 19132 ? Host : $"{Host}:{Port}";
}

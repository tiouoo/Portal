using System.Text;
using Portal.Bedrock.Standard.Manifest;
using Portal.Core.Minecraft.Instance.Bedrock;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Services;

/// <summary>
/// 基岩版服务器列表（external_servers.txt）管理服务。
/// 文件按用户 ID 区分存储：&lt;com.mojang&gt;/minecraftpe/external_servers.txt。
/// 与 BedrockBoot 的服务器列表保持一致，使用分号分隔记录：name;address;icon;hidden。
/// </summary>
public static class BedrockServerManager
{
    private const int DefaultPort = 19132;
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
    /// 向指定用户 ID 的服务器列表添加服务器。
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
                lines.Add(BuildLine(name.Trim(), address.Trim(), string.Empty, false));
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

                lines[lineIndex] = BuildLine(name.Trim(), address.Trim(), existing.IconText, existing.Hidden);
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

    private static string BuildLine(string name, string address, string iconText, bool hidden) =>
        $"{name};{address};{iconText};{(hidden ? 1 : 0)}";

    private static void WriteLines(string path, IReadOnlyList<string> lines) =>
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
}

/// <summary>
/// external_servers.txt 中的一条服务器记录。
/// </summary>
public sealed class BedrockServerEntry
{
    public int LineIndex { get; set; }
    public required string Name { get; set; }
    public required string Address { get; set; }
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 19132;
    public string IconText { get; init; } = string.Empty;
    public byte[]? IconData { get; init; }
    public bool Hidden { get; init; }

    public string DisplayAddress => Port == 19132 ? Host : $"{Host}:{Port}";
}

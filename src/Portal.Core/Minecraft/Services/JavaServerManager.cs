using fNbt;
using Portal.Core.Minecraft.Classes;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Services;

/// <summary>
/// Java 实例服务器列表（servers.dat）管理服务。
/// 与 PCLCE 的 PageInstanceServer 一致，直接读写实例 .minecraft 下的 servers.dat 文件，
/// 而不是通过解析游戏日志获取服务器。
/// </summary>
public static class JavaServerManager
{
    private static readonly object FileLock = new();

    public static string GetServersDatPath(MinecraftInstance instance) =>
        Path.Combine(instance.MinecraftEntry!.MinecraftFolderPath, "servers.dat");

    public static bool IsSupported(MinecraftInstance instance) =>
        instance.IsJava && instance.MinecraftEntry != null;

    /// <summary>
    /// 从 servers.dat 读取服务器列表。
    /// </summary>
    public static IReadOnlyList<MinecraftServerEntry> Read(MinecraftInstance instance)
    {
        if (!IsSupported(instance))
            return [];

        lock (FileLock)
        {
            var path = GetServersDatPath(instance);
            if (!File.Exists(path))
                return [];

            try
            {
                var file = new NbtFile();
                file.LoadFromFile(path);
                return (file.RootTag["servers"] as NbtList)?.OfType<NbtCompound>()
                    .Select(CreateEntry)
                    .Where(entry => entry != null)
                    .Cast<MinecraftServerEntry>()
                    .ToArray() ?? [];
            }
            catch (Exception exception)
            {
                Logger.Warning($"读取服务器列表失败：{path}{Environment.NewLine}{exception}");
                return [];
            }
        }
    }

    /// <summary>
    /// 添加服务器到 servers.dat。
    /// </summary>
    public static bool Add(MinecraftInstance instance, string name, string address)
    {
        if (!IsSupported(instance))
            return false;

        lock (FileLock)
        {
            try
            {
                var path = GetServersDatPath(instance);
                var file = new NbtFile();
                if (File.Exists(path))
                {
                    file.LoadFromFile(path);
                }

                if (file.RootTag["servers"] is not NbtList list)
                {
                    list = new NbtList("servers", NbtTagType.Compound);
                    file.RootTag["servers"] = list;
                }

                if (list.ListType != NbtTagType.Compound)
                    list.ListType = NbtTagType.Compound;

                var compound = new NbtCompound();
                compound["name"] = new NbtString("name", name);
                compound["ip"] = new NbtString("ip", address);
                list.Add(compound);
                file.SaveToFile(path, NbtCompression.GZip);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Warning($"添加服务器失败：{name} {address}{Environment.NewLine}{exception}");
                return false;
            }
        }
    }

    /// <summary>
    /// 编辑 servers.dat 中指定序号的服务器（保留图标等其余字段）。
    /// </summary>
    public static bool Update(MinecraftInstance instance, int index, string name, string address)
    {
        if (!IsSupported(instance))
            return false;

        lock (FileLock)
        {
            try
            {
                var path = GetServersDatPath(instance);
                if (!File.Exists(path))
                    return false;

                var file = new NbtFile();
                file.LoadFromFile(path);
                if (file.RootTag["servers"] is not NbtList { } list ||
                    index < 0 || index >= list.Count ||
                    list[index] is not NbtCompound server)
                    return false;

                server["name"] = new NbtString("name", name);
                server["ip"] = new NbtString("ip", address);
                file.SaveToFile(path, NbtCompression.GZip);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Warning($"编辑服务器失败：{name} {address}{Environment.NewLine}{exception}");
                return false;
            }
        }
    }

    /// <summary>
    /// 从 servers.dat 删除指定序号的服务器。
    /// </summary>
    public static bool Remove(MinecraftInstance instance, int index)
    {
        if (!IsSupported(instance))
            return false;

        lock (FileLock)
        {
            try
            {
                var path = GetServersDatPath(instance);
                if (!File.Exists(path))
                    return false;

                var file = new NbtFile();
                file.LoadFromFile(path);
                if (file.RootTag["servers"] is not NbtList { } list ||
                    index < 0 || index >= list.Count)
                    return false;

                list.RemoveAt(index);
                file.SaveToFile(path, NbtCompression.GZip);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Warning($"删除服务器失败：{index}{Environment.NewLine}{exception}");
                return false;
            }
        }
    }

    private static MinecraftServerEntry? CreateEntry(NbtCompound server)
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
            try
            {
                icon = Convert.FromBase64String(encoded);
            }
            catch (FormatException exception)
            {
                Logger.Warning($"服务器图标 Base64 数据无效，将忽略图标。{Environment.NewLine}{exception}");
            }
        }

        return new MinecraftServerEntry
        {
            Name = (server["name"] as NbtString)?.Value ?? host,
            Address = address,
            Host = host,
            Port = port,
            IconData = icon,
            Hidden = (server["hidden"] as NbtByte)?.Value == 1
        };
    }

    /// <summary>
    /// 解析服务器地址，支持 [addr]:port 形式的 IPv6、host:port 与默认端口。
    /// </summary>
    public static (string Host, int Port) ParseAddress(string address)
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
}

/// <summary>
/// servers.dat 中的一条服务器记录。
/// </summary>
public sealed class MinecraftServerEntry
{
    public required string Name { get; set; }
    public required string Address { get; set; }
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 25565;
    public byte[]? IconData { get; set; }
    public bool Hidden { get; set; }

    public string DisplayAddress => Port == 25565 ? Host : $"{Host}:{Port}";
}

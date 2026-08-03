using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using Avalonia.Media.Imaging;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Module.DesktopShortcut;

/// <summary>
/// 在桌面创建快捷方式，通过 portal://launch?id=...&amp;folder=... 协议链接启动实例，
/// 也可通过附加 world / server 参数直接进入世界或服务器。
/// Windows：写 .url Internet 快捷方式；Linux：写可执行的 .desktop 启动器；macOS：写 .webloc。
/// </summary>
public static class DesktopShortcutService
{
    /// <summary>构造指向指定实例的 portal://launch 链接。</summary>
    public static string BuildLaunchUrl(MinecraftInstance instance)
    {
        var id = instance.MinecraftEntry?.Id
                 ?? Path.GetFileName(instance.InstanceFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return $"portal://launch?id={Uri.EscapeDataString(id)}&folder={Uri.EscapeDataString(instance.FolderPath)}";
    }

    /// <summary>构造直接进入指定世界（saves 下的文件夹名）的 portal://launch 链接。</summary>
    public static string BuildWorldLaunchUrl(MinecraftInstance instance, string worldFolder) =>
        $"{BuildLaunchUrl(instance)}&world={Uri.EscapeDataString(worldFolder)}";

    /// <summary>构造直接进入指定服务器的 portal://launch 链接。</summary>
    public static string BuildServerLaunchUrl(MinecraftInstance instance, string address, int port) =>
        $"{BuildLaunchUrl(instance)}&server={Uri.EscapeDataString(address)}&port={port}";

    /// <summary>在桌面创建快捷方式，返回快捷方式文件路径。</summary>
    public static Task<string> CreateAsync(MinecraftInstance instance) =>
        CreateAsync(instance, BuildLaunchUrl(instance), instance.InstanceName, instance.Icons[256] as Bitmap);

    /// <summary>为最近游玩目标（世界 / 服务器）在桌面创建快捷方式，返回快捷方式文件路径。</summary>
    public static Task<string> CreateAsync(MinecraftInstance instance, RecentPlayTarget target)
    {
        var url = target.Type switch
        {
            RecentPlayTargetType.World when !string.IsNullOrWhiteSpace(target.Id) => BuildWorldLaunchUrl(instance, target.Id),
            RecentPlayTargetType.Server when !string.IsNullOrWhiteSpace(target.ServerAddress) =>
                BuildServerLaunchUrl(instance, target.ServerAddress, target.ServerPort ?? 25565),
            _ => BuildLaunchUrl(instance)
        };

        var name = target.Type == RecentPlayTargetType.World && !string.IsNullOrWhiteSpace(target.Id) &&
                   !string.Equals(target.Name, target.Id, StringComparison.Ordinal)
            ? $"{target.Name} ({target.Id})"
            : target.Name;
        var icon = TryLoadIcon(target.WorldIconPath, target.ServerIconData) ?? (instance.Icons[256] as Bitmap);
        return CreateAsync(instance, url, $"{instance.InstanceName} - {name}", icon);
    }

    private static async Task<string> CreateAsync(MinecraftInstance instance, string url, string displayName, Bitmap? icon)
    {
        if (OperatingSystem.IsWindows()) return CreateWindowsShortcut(url, displayName, icon);
        if (OperatingSystem.IsLinux()) return await CreateLinuxShortcutAsync(url, displayName, icon);
        if (OperatingSystem.IsMacOS()) return CreateMacShortcut(url, displayName);
        throw new PlatformNotSupportedException("当前系统暂不支持创建桌面快捷方式。");
    }

    private static Bitmap? TryLoadIcon(string? path, byte[]? data)
    {
        try
        {
            if (data is { Length: > 0 })
            {
                using var stream = new MemoryStream(data);
                return Bitmap.DecodeToWidth(stream, 256);
            }

            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, 256);
            }
        }
        catch (Exception)
        {
            // 图标只是锦上添花，读取失败时退回实例图标。
        }

        return null;
    }

    private static string GetDesktopDirectory()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (Directory.Exists(desktop)) return desktop;

        // 部分 Linux 桌面未正确配置 XDG，DesktopDirectory 可能返回不存在的 ~/Desktop，
        // 再兜底尝试常见桌面目录（含中文系统下的“桌面”）。
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var candidate in new[] { Path.Combine(home, "Desktop"), Path.Combine(home, "桌面") })
        {
            if (Directory.Exists(candidate)) return candidate;
        }

        return desktop;
    }

    private static string GetExecutablePath()
    {
        // AppImage 的 ProcessPath 指向临时挂载点，退出后失效；运行时通过 APPIMAGE 环境变量给出包文件本身的路径。
        if (Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImagePath && File.Exists(appImagePath))
            return appImagePath;
        return Environment.ProcessPath ?? throw new InvalidOperationException("无法确定启动器可执行文件路径。");
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder();
        foreach (var c in name)
            builder.Append(invalid.Contains(c) ? '_' : c);
        var result = builder.ToString().Trim();
        return string.IsNullOrEmpty(result) ? "Portal" : result;
    }

    private static string CreateWindowsShortcut(string url, string displayName, Bitmap? icon)
    {
        var desktop = GetDesktopDirectory();
        var safeName = SanitizeFileName(displayName);
        var path = Path.Combine(desktop, $"{safeName}.url");

        var builder = new StringBuilder();
        builder.AppendLine("[InternetShortcut]");
        builder.AppendLine($"URL={url}");

        // 优先用目标图标，写失败时退回启动器图标，仍能正常创建快捷方式。
        var iconFile = TryWriteIcon(icon, EncodeIco, ".ico", safeName) ?? GetExecutablePath();
        builder.AppendLine($"IconFile={iconFile}");
        builder.AppendLine("IconIndex=0");

        File.WriteAllText(path, builder.ToString());
        Logger.Info($"已创建桌面快捷方式：{path}");
        return path;
    }

    private static async Task<string> CreateLinuxShortcutAsync(string url, string displayName, Bitmap? icon)
    {
        var desktop = GetDesktopDirectory();
        if (!Directory.Exists(desktop))
            throw new DirectoryNotFoundException($"未找到桌面文件夹：{desktop}");

        var safeName = SanitizeFileName(displayName);
        var path = Path.Combine(desktop, $"{safeName}.desktop");

        var iconPath = TryWriteIcon(icon, EncodePng, ".png", safeName);

        var builder = new StringBuilder();
        builder.AppendLine("[Desktop Entry]");
        builder.AppendLine("Type=Application");
        builder.AppendLine($"Name={EscapeDesktopValue(displayName)}");
        builder.AppendLine($"Comment=通过 Portal 启动 {EscapeDesktopValue(displayName)}");
        builder.AppendLine($"Exec={EscapeDesktopExec(GetExecutablePath())} {EscapeDesktopExec(url)}");
        if (iconPath != null) builder.AppendLine($"Icon={iconPath}");
        builder.AppendLine("Terminal=false");
        builder.AppendLine("Categories=Game;");
        await File.WriteAllTextAsync(path, builder.ToString());

        // 桌面文件需要可执行权限才会被文件管理器识别为可启动的启动器。
        await RunProcessAsync("chmod", ["+x", path], required: false);
        Logger.Info($"已创建桌面快捷方式：{path}");
        return path;
    }

    private static string CreateMacShortcut(string url, string displayName)
    {
        var desktop = GetDesktopDirectory();
        var path = Path.Combine(desktop, $"{SanitizeFileName(displayName)}.webloc");

        var plist = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>URL</key>
                <string>{SecurityElement.Escape(url)}</string>
            </dict>
            </plist>
            """;
        File.WriteAllText(path, plist);
        Logger.Info($"已创建桌面快捷方式：{path}");
        return path;
    }

    private static string? TryWriteIcon(Bitmap? icon, Func<Bitmap, byte[]> encoder, string extension, string name)
    {
        try
        {
            if (icon == null) return null;

            var bytes = encoder(icon);
            var folder = Path.Combine(ConfigPath.UserDataRootPath, "DesktopShortcuts");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{SanitizeFileName(name)}{extension}");
            File.WriteAllBytes(path, bytes);
            return path;
        }
        catch (Exception e)
        {
            // 图标只是锦上添花，写失败不影响快捷方式本身。
            Logger.Error("写入快捷方式图标失败。", e);
            return null;
        }
    }

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    private static byte[] EncodeIco(Bitmap bitmap)
    {
        var png = EncodePng(bitmap);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((short)0);   // 保留字段
            writer.Write((short)1);   // 类型：图标
            writer.Write((short)1);   // 图标数量
            var size = bitmap.PixelSize;
            writer.Write((byte)(size.Width >= 256 ? 0 : size.Width));
            writer.Write((byte)(size.Height >= 256 ? 0 : size.Height));
            writer.Write((byte)0);    // 颜色数
            writer.Write((byte)0);    // 保留
            writer.Write((short)1);   // 颜色平面
            writer.Write((short)32);  // 位深
            writer.Write(png.Length); // 数据长度
            writer.Write(22);         // 数据偏移（6 字节 ICONDIR + 16 字节目录项）
            writer.Write(png);
        }
        return stream.ToArray();
    }

    /// <summary>转义 desktop entry 的普通值（去除换行、转义反斜杠）。</summary>
    private static string EscapeDesktopValue(string value) =>
        value.Replace("\\", "\\\\").Replace("\r", string.Empty).Replace("\n", string.Empty);

    /// <summary>转义 desktop entry Exec 参数：% 是保留字段码前缀需双写，含空格的参数用双引号包裹。</summary>
    private static string EscapeDesktopExec(string value)
    {
        value = value.Replace("\\", "\\\\").Replace("%", "%%");
        return $"\"{value}\"";
    }

    private static async Task RunProcessAsync(string fileName, string[] arguments, bool required)
    {
        try
        {
            var startInfo = new ProcessStartInfo { FileName = fileName, UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException($"无法启动 {fileName}。");
            await process.WaitForExitAsync();
            if (required && process.ExitCode != 0)
                throw new InvalidOperationException($"{fileName} 执行失败（退出码 {process.ExitCode}）。");
        }
        catch (Exception) when (!required)
        {
        }
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Avalonia.Media.Imaging;
using Portal.Core.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Module;

public static class DesktopShortcutService
{
    public static string BuildLaunchUrl(MinecraftInstance instance)
    {
        var id = instance.MinecraftEntry?.Id
                 ?? Path.GetFileName(instance.InstanceFolderPath.TrimEnd(Path.DirectorySeparatorChar,
                     Path.AltDirectorySeparatorChar));
        return $"portal://launch?id={Uri.EscapeDataString(id)}&folder={Uri.EscapeDataString(instance.FolderPath)}";
    }

    public static string BuildWorldLaunchUrl(MinecraftInstance instance, string worldFolder)
    {
        return $"{BuildLaunchUrl(instance)}&world={Uri.EscapeDataString(worldFolder)}";
    }

    public static string BuildServerLaunchUrl(MinecraftInstance instance, string address, int port)
    {
        return $"{BuildLaunchUrl(instance)}&server={Uri.EscapeDataString(address)}&port={port}";
    }

    public static Task<string> CreateAsync(MinecraftInstance instance)
    {
        return CreateAsync(instance, BuildLaunchUrl(instance), instance.InstanceName,
            instance.Icons[256]);
    }

    public static Task<string> CreateAsync(MinecraftInstance instance, RecentPlayTarget target)
    {
        var url = target.Type switch
        {
            RecentPlayTargetType.World when !string.IsNullOrWhiteSpace(target.Id) => BuildWorldLaunchUrl(instance,
                target.Id),
            RecentPlayTargetType.Server when !string.IsNullOrWhiteSpace(target.ServerAddress) =>
                BuildServerLaunchUrl(instance, target.ServerAddress, target.ServerPort ?? 25565),
            _ => BuildLaunchUrl(instance)
        };

        var name = target.Type == RecentPlayTargetType.World && !string.IsNullOrWhiteSpace(target.Id) &&
                   !string.Equals(target.Name, target.Id, StringComparison.Ordinal)
            ? $"{target.Name} ({target.Id})"
            : target.Name;
        var icon = TryLoadIcon(target.WorldIconPath, target.ServerIconData) ?? instance.Icons[256];
        return CreateAsync(instance, url, $"{instance.InstanceName} - {name}", icon);
    }

    private static async Task<string> CreateAsync(MinecraftInstance instance, string url, string displayName,
        Bitmap? icon)
    {
        if (OperatingSystem.IsWindows()) return CreateWindowsShortcut(url, displayName, icon);
        if (OperatingSystem.IsLinux()) return await CreateLinuxShortcutAsync(url, displayName, icon);
        if (OperatingSystem.IsMacOS()) return CreateMacShortcut(url, displayName);
        throw new PlatformNotSupportedException(CommonLanguageManager.Instance.desktop_notSupportedCreateShortcut.CurrentValue());
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
        }

        return null;
    }

    private static string GetDesktopDirectory()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (Directory.Exists(desktop)) return desktop;


        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var candidate in new[] { Path.Combine(home, "Desktop"), Path.Combine(home, LinguaSentinels.DesktopDirectory) })
            if (Directory.Exists(candidate))
                return candidate;

        return desktop;
    }

    private static string GetExecutablePath()
    {
        if (Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImagePath && File.Exists(appImagePath))
            return appImagePath;
        return Environment.ProcessPath ?? throw new InvalidOperationException(CommonLanguageManager.Instance.common_cannotDetermineExecutablePath.CurrentValue());
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
        var path = Path.Combine(desktop, $"{safeName}.lnk");


        var iconFile = TryWriteIcon(icon, EncodeIco, ".ico", safeName) ?? GetExecutablePath();

        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(GetExecutablePath());
            link.SetArguments(url);
            link.SetDescription(string.Format(CommonLanguageManager.Instance.desktop_launchViaPortal.CurrentValue(), displayName));
            link.SetWorkingDirectory(Path.GetDirectoryName(GetExecutablePath()) ?? string.Empty);
            link.SetIconLocation(iconFile, 0);

            ((IPersistFile)link).Save(path, false);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }

        Logger.Info(string.Format(LogLanguageManager.Instance.desktop_shortcutCreated.CurrentValue(), path));
        return path;
    }

    private static async Task<string> CreateLinuxShortcutAsync(string url, string displayName, Bitmap? icon)
    {
        var desktop = GetDesktopDirectory();
        if (!Directory.Exists(desktop))
            throw new DirectoryNotFoundException(string.Format(CommonLanguageManager.Instance.desktop_desktopFolderNotFound.CurrentValue(), desktop));

        var safeName = SanitizeFileName(displayName);
        var path = Path.Combine(desktop, $"{safeName}.desktop");

        var iconPath = TryWriteIcon(icon, EncodePng, ".png", safeName);

        var builder = new StringBuilder();
        builder.AppendLine("[Desktop Entry]");
        builder.AppendLine("Type=Application");
        builder.AppendLine($"Name={EscapeDesktopValue(displayName)}");
        builder.AppendLine($"Comment={string.Format(CommonLanguageManager.Instance.desktop_launchViaPortal.CurrentValue(), EscapeDesktopValue(displayName))}");
        builder.AppendLine($"Exec={EscapeDesktopExec(GetExecutablePath())} {EscapeDesktopExec(url)}");
        if (iconPath != null) builder.AppendLine($"Icon={iconPath}");
        builder.AppendLine("Terminal=false");
        builder.AppendLine("Categories=Game;");
        await File.WriteAllTextAsync(path, builder.ToString());


        await RunProcessAsync("chmod", ["+x", path], false);
        Logger.Info(string.Format(LogLanguageManager.Instance.desktop_shortcutCreated.CurrentValue(), path));
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
        Logger.Info(string.Format(LogLanguageManager.Instance.desktop_shortcutCreated.CurrentValue(), path));
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
            Logger.Error(LogLanguageManager.Instance.desktop_iconWriteFailed.CurrentValue(), e);
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
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write((short)0);
            writer.Write((short)1);
            writer.Write((short)1);
            var size = bitmap.PixelSize;
            writer.Write((byte)(size.Width >= 256 ? 0 : size.Width));
            writer.Write((byte)(size.Height >= 256 ? 0 : size.Height));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(png.Length);
            writer.Write(22);
            writer.Write(png);
        }

        return stream.ToArray();
    }

    private static string EscapeDesktopValue(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\r", string.Empty).Replace("\n", string.Empty);
    }

    private static string EscapeDesktopExec(string value)
    {
        value = value.Replace("\\", "\\\\").Replace("%", "%%");
        return $"\"{value}\"";
    }

    private static async Task RunProcessAsync(string fileName, string[] arguments, bool required)
    {
        try
        {
            var startInfo = new ProcessStartInfo
                { FileName = fileName, UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException(string.Format(CommonLanguageManager.Instance.common_cannotStart.CurrentValue(), fileName));
            await process.WaitForExitAsync();
            if (required && process.ExitCode != 0)
                throw new InvalidOperationException(string.Format(CommonLanguageManager.Instance.common_executeFailed.CurrentValue(), fileName, process.ExitCode));
        }
        catch (Exception) when (!required)
        {
        }
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData,
            uint flags);

        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);

        void GetIconLocation([Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxPath,
            out int iconIndex);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr windowHandle, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [ComImport]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }
}
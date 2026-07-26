using System.ComponentModel;
using System.Diagnostics;
using Portal.Const;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Module.Ipc;

/// <summary>
/// 注册 portal:// URL 协议，让浏览器可以调起启动器。
/// Windows：把内嵌的注册脚本写到临时文件夹，申请管理员权限运行，写入 HKEY_CLASSES_ROOT。
/// Linux：写 ~/.local/share/applications 下的 .desktop 处理器并调用 xdg-mime，无需管理员权限。
/// macOS：URL 协议须在 .app 的 Info.plist 中声明，无法在运行时注册。
/// </summary>
public static class ProtocolRegistration
{
    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    private const string WindowsScriptTemplate =
        """
        @echo off
        chcp 65001 >nul
        reg add "HKEY_CLASSES_ROOT\portal" /f /ve /d "URL:Portal Protocol" || goto :fail
        reg add "HKEY_CLASSES_ROOT\portal" /f /v "URL Protocol" /d "" || goto :fail
        reg add "HKEY_CLASSES_ROOT\portal\DefaultIcon" /f /ve /d "\"__PORTAL_EXE__\",0" || goto :fail
        reg add "HKEY_CLASSES_ROOT\portal\shell\open\command" /f /ve /d "\"__PORTAL_EXE__\" \"%%1\"" || goto :fail
        exit /b 0
        :fail
        exit /b 1
        """;

    private const string LinuxDesktopTemplate =
        """
        [Desktop Entry]
        Type=Application
        Name=Portal
        Comment=Portal URL protocol handler
        Exec="__PORTAL_EXE__" %u
        Terminal=false
        NoDisplay=true
        MimeType=x-scheme-handler/portal;
        """;

    public static async Task RegisterAsync()
    {
        var executablePath = Environment.ProcessPath
                             ?? throw new InvalidOperationException("无法确定启动器可执行文件路径。");
        if (OperatingSystem.IsWindows()) await RegisterWindowsAsync(executablePath);
        else if (OperatingSystem.IsLinux()) await RegisterLinuxAsync(executablePath);
        else throw new PlatformNotSupportedException("当前系统暂不支持注册 portal:// 协议。");
    }

    private static async Task RegisterWindowsAsync(string executablePath)
    {
        Directory.CreateDirectory(ConfigPath.TempFolderPath);
        var scriptPath = Path.Combine(ConfigPath.TempFolderPath, "RegisterPortalProtocol.bat");
        await File.WriteAllTextAsync(scriptPath, WindowsScriptTemplate.Replace("__PORTAL_EXE__", executablePath));
        Logger.Info($"已写出协议注册脚本：{scriptPath}");

        Process process;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = scriptPath,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            }) ?? throw new InvalidOperationException("无法启动协议注册脚本。");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223) // ERROR_CANCELLED
        {
            throw new OperationCanceledException("未获得管理员权限。");
        }

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"注册脚本执行失败（退出码 {process.ExitCode}）。");
    }

    private static async Task RegisterLinuxAsync(string executablePath)
    {
        // Linux 上 LocalApplicationData 即 $XDG_DATA_HOME（默认 ~/.local/share）。
        var applicationsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "applications");
        Directory.CreateDirectory(applicationsFolder);
        const string desktopFileName = "xyz.tiouo.Portal.url-handler.desktop";
        var desktopFilePath = Path.Combine(applicationsFolder, desktopFileName);
        await File.WriteAllTextAsync(desktopFilePath,
            LinuxDesktopTemplate.Replace("__PORTAL_EXE__", executablePath) + "\n");
        Logger.Info($"已写出协议处理器：{desktopFilePath}");

        await RunProcessAsync("xdg-mime", ["default", desktopFileName, "x-scheme-handler/portal"], required: true);
        // 刷新桌面数据库属于锦上添花，失败不影响 xdg-mime 的注册结果。
        await RunProcessAsync("update-desktop-database", [applicationsFolder], required: false);
    }

    private static async Task RunProcessAsync(string fileName, string[] arguments, bool required)
    {
        try
        {
            var startInfo = new ProcessStartInfo { FileName = fileName, UseShellExecute = false };
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

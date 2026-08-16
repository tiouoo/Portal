using System.ComponentModel;
using System.Diagnostics;
using Portal.Core.Const;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Module.Ipc;

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
        Exec=__PORTAL_EXE__ %u
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

        public static async Task TryRegisterLinuxOnStartupAsync()
    {
        if (!OperatingSystem.IsLinux()) return;

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) ||
            string.Equals(Path.GetFileNameWithoutExtension(executablePath), "dotnet",
                StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            await RegisterLinuxAsync(executablePath);
        }
        catch (Exception exception)
        {
            
            Logger.Warning("Linux portal:// 协议自动注册失败，可在设置中重试。" +
                           Environment.NewLine + exception);
        }
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
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223) 
        {
            throw new OperationCanceledException("未获得管理员权限。");
        }

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"注册脚本执行失败（退出码 {process.ExitCode}）。");
    }

    private static async Task RegisterLinuxAsync(string executablePath)
    {
        
        if (Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImagePath &&
            File.Exists(appImagePath))
            executablePath = appImagePath;

        
        var applicationsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "applications");
        Directory.CreateDirectory(applicationsFolder);
        const string desktopFileName = "cc.tiouo.Portal.url-handler.desktop";
        var desktopFilePath = Path.Combine(applicationsFolder, desktopFileName);
        await File.WriteAllTextAsync(desktopFilePath,
            LinuxDesktopTemplate.Replace("__PORTAL_EXE__", EscapeDesktopExecArgument(executablePath)) + "\n");
        Logger.Info($"已写出协议处理器：{desktopFilePath}");

        await RunProcessAsync("xdg-mime", ["default", desktopFileName, "x-scheme-handler/portal"], required: true);
        
        await RunProcessAsync("update-desktop-database", [applicationsFolder], required: false);
    }

    private static string EscapeDesktopExecArgument(string value) => '"' + value
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("$", "\\$")
        .Replace("`", "\\`")
        .Replace("%", "%%") + '"';

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

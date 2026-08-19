using System.ComponentModel;
using System.Diagnostics;
using Portal.Core.Const;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Module.Ipc;

public static class ProtocolRegistration
{
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

    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    public static async Task RegisterAsync()
    {
        var executablePath = Environment.ProcessPath
                             ?? throw new InvalidOperationException(CommonLanguageManager.Instance.common_cannotDetermineExecutablePath.CurrentValue());
        if (OperatingSystem.IsWindows()) await RegisterWindowsAsync(executablePath);
        else if (OperatingSystem.IsLinux()) await RegisterLinuxAsync(executablePath);
        else throw new PlatformNotSupportedException(CommonLanguageManager.Instance.ipc_unsupportedProtocolRegistration.CurrentValue());
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
            Logger.Warning(string.Format(LogLanguageManager.Instance.ipc_linuxAutoRegisterFailed.CurrentValue(), Environment.NewLine, exception));
        }
    }

    private static async Task RegisterWindowsAsync(string executablePath)
    {
        Directory.CreateDirectory(ConfigPath.TempFolderPath);
        var scriptPath = Path.Combine(ConfigPath.TempFolderPath, "RegisterPortalProtocol.bat");
        await File.WriteAllTextAsync(scriptPath, WindowsScriptTemplate.Replace("__PORTAL_EXE__", executablePath));
        Logger.Info(string.Format(LogLanguageManager.Instance.ipc_scriptWritten.CurrentValue(), scriptPath));

        Process process;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = scriptPath,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            }) ?? throw new InvalidOperationException(CommonLanguageManager.Instance.ipc_cannotStartRegistrationScript.CurrentValue());
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException(CommonLanguageManager.Instance.ipc_adminPrivilegeNotObtained.CurrentValue());
        }

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.Format(CommonLanguageManager.Instance.ipc_scriptExecutionFailed.CurrentValue(), process.ExitCode));
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
        Logger.Info(string.Format(LogLanguageManager.Instance.ipc_desktopHandlerWritten.CurrentValue(), desktopFilePath));

        await RunProcessAsync("xdg-mime", ["default", desktopFileName, "x-scheme-handler/portal"], true);

        await RunProcessAsync("update-desktop-database", [applicationsFolder], false);
    }

    private static string EscapeDesktopExecArgument(string value)
    {
        return '"' + value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("$", "\\$")
            .Replace("`", "\\`")
            .Replace("%", "%%") + '"';
    }

    private static async Task RunProcessAsync(string fileName, string[] arguments, bool required)
    {
        try
        {
            var startInfo = new ProcessStartInfo { FileName = fileName, UseShellExecute = false };
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
}
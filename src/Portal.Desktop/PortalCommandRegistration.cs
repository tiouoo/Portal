using System.Diagnostics;
using System.Text;
using Portal.Core.Const;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Desktop;

internal static class PortalCommandRegistration
{
    public const string CommandName = "portal";

    private const string WindowsCmdTemplate =
        """
        @echo off
        if /I "%1"=="--version" goto :headless
        if /I "%1"=="-V" goto :headless
        if /I "%1"=="-l" goto :headless
        if /I "%1"=="--list" goto :headless
        if /I "%1"=="list" goto :headless
        if /I "%1"=="search" goto :headless
        if /I "%1"=="help" goto :headless
        if /I "%1"=="--help" goto :headless
        if /I "%1"=="-h" goto :headless
        if /I "%1"=="/?" goto :headless
        if /I "%1"=="-?" goto :headless
        "__PORTAL_EXE__" %*
        exit /b 0
        :headless
        chcp 65001 >nul
        powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0portal.ps1" %*
        exit /b %errorlevel%
        """;

    private const string WindowsPsTemplate =
        """
        try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}
        $exe = '__PORTAL_EXE__'
        $argLine = ($args | ForEach-Object {
            if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
        }) -join ' '
        $process = Start-Process -FilePath $exe -ArgumentList $argLine -NoNewWindow -Wait -PassThru
        exit $process.ExitCode
        """;

    private const string UnixShTemplate =
        """
        #!/bin/sh
        exec "__PORTAL_EXE__" "$@"
        """;

    public static async Task RegisterAsync()
    {
        try
        {
            await RegisterCoreAsync();
        }
        catch (Exception exception)
        {
            Logger.Warning(string.Format(LogLanguageManager.Instance.desktop_cli_registerFailed.CurrentValue(),
                Environment.NewLine, exception));
        }
    }

    private static async Task RegisterCoreAsync()
    {
        var executablePath = ResolveExecutablePath();
        if (executablePath is null)
            return;

        if (OperatingSystem.IsWindows())
        {
            var binDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Portal", "bin");
            Directory.CreateDirectory(binDir);
            await File.WriteAllTextAsync(Path.Combine(binDir, "portal.cmd"),
                WindowsCmdTemplate.Replace("__PORTAL_EXE__", executablePath), new UTF8Encoding(false));
            await File.WriteAllTextAsync(Path.Combine(binDir, "portal.ps1"),
                WindowsPsTemplate.Replace("__PORTAL_EXE__", executablePath.Replace("'", "''")), new UTF8Encoding(true));
            EnsureUserPath(binDir);
#if WINDOWS
            RegisterAppPaths(executablePath);
#endif
        }
        else
        {
            var binDir = ChooseUnixBinDir();
            Directory.CreateDirectory(binDir);
            var shimPath = Path.Combine(binDir, CommandName);
            await File.WriteAllTextAsync(shimPath,
                UnixShTemplate.Replace("__PORTAL_EXE__", EscapeShellArgument(executablePath)),
                new UTF8Encoding(false));
            MakeExecutable(shimPath);
            EnsureUnixPath(binDir);
        }

        Logger.Info(string.Format(LogLanguageManager.Instance.desktop_cli_registered.CurrentValue(),
            CommandName, executablePath));
    }

    private static string? ResolveExecutablePath()
    {
        if (OperatingSystem.IsLinux() &&
            Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage &&
            File.Exists(appImage))
            return appImage;

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            return null;
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            return null;

        return Path.GetFullPath(processPath);
    }

    private static string ChooseUnixBinDir()
    {
        const string systemBin = "/usr/local/bin";
        try
        {
            Directory.CreateDirectory(systemBin);
            var probe = Path.Combine(systemBin, $".portal-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return systemBin;
        }
        catch
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin");
        }
    }

    private static void MakeExecutable(string shimPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "chmod",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("+x");
            startInfo.ArgumentList.Add(shimPath);
            using var process = Process.Start(startInfo);
            process?.WaitForExit(2000);
        }
        catch
        {
        }
    }

    private static void EnsureUserPath(string binDir)
    {
        var userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? string.Empty;
        var entries = userPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Any(entry => string.Equals(entry, binDir, StringComparison.OrdinalIgnoreCase)))
            return;

        var newPath = userPath.TrimEnd(';') + ";" + binDir;
        Environment.SetEnvironmentVariable("Path", newPath, EnvironmentVariableTarget.User);
    }

    private static void EnsureUnixPath(string binDir)
    {
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (currentPath.Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Any(entry => string.Equals(entry.TrimEnd('/'), binDir.TrimEnd('/'), StringComparison.Ordinal)))
            return;

        if (string.Equals(binDir.TrimEnd('/'), "/usr/local/bin", StringComparison.Ordinal))
            return;

        var profilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".profile");
        var line = "export PATH=\"" + binDir + ":$PATH\"";
        try
        {
            var existing = File.Exists(profilePath) ? File.ReadAllText(profilePath) : string.Empty;
            if (existing.Contains(line, StringComparison.Ordinal))
                return;
            File.AppendAllText(profilePath, Environment.NewLine + "# Portal CLI command" + Environment.NewLine +
                                           line + Environment.NewLine);
        }
        catch (Exception exception)
        {
            Logger.Warning(string.Format(LogLanguageManager.Instance.desktop_cli_pathUpdateFailed.CurrentValue(),
                Environment.NewLine, exception));
        }
    }

#if WINDOWS
    private static void RegisterAppPaths(string executablePath)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\App Paths\portal.exe");
            key?.SetValue("", executablePath);
        }
        catch (Exception exception)
        {
            Logger.Warning(string.Format(LogLanguageManager.Instance.desktop_cli_registerFailed.CurrentValue(),
                Environment.NewLine, exception));
        }
    }
#endif

    private static string EscapeShellArgument(string value)
    {
        return '"' + value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("$", "\\$")
            .Replace("`", "\\`") + '"';
    }
}

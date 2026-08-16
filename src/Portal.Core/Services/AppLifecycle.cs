using System;
using System.Diagnostics;
using System.IO;
using Portal.Module.Initialize;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Events;

namespace Portal.Services;

public static class AppLifecycle
{
    public static async void RestartApp(bool isAdmin = false)
    {
        if (!await ApplicationEvents.RaiseAppExiting()) return;
        ConfigSaver.FlushConfig();
        var fileName = Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImagePath &&
                       File.Exists(appImagePath)
            ? appImagePath
            : Process.GetCurrentProcess().MainModule.FileName;
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.CurrentDirectory,
            FileName = fileName
        };
        if (isAdmin) startInfo.Verb = "runas";
        Process.Start(startInfo);
        Environment.Exit(0);
    }

    public static async void TryExitApp()
    {
        if (!await ApplicationEvents.RaiseAppExiting()) return;
        ConfigSaver.FlushConfig();
        Environment.Exit(0);
    }
}

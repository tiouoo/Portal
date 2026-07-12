using System;
using System.Runtime.InteropServices;
using Avalonia;
using HotAvalonia;
using Tio.Avalonia.Standard.Modules;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Initializer.Program("Portal", "xyz.tiouo.Portal");
        Logger.Info("应用程序启动 Main()");
        
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Logger.Info("Running on Windows");
        else if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Logger.Info("Running on Linux");
        else if(RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Logger.Info("Running on macOS");
        
        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Logger.Fatal(ex);
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .UseHotReload()
            // .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
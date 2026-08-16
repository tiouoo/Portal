using System.Text;
using Avalonia;
using Portal.Core.Services.SystemResources;
using Tio.Avalonia.Standard.Modules.DiskIO;
#if DEBUG
using HotAvalonia;
#endif

namespace Portal.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        Logger.Info("Portal MC");
        Logger.Info(@"  ____                   _             _     __  __    ____ ");
        Logger.Info(@" |  _ \    ___    _ __  | |_    __ _  | |   |  \/  |  / ___|");
        Logger.Info(@" | |_) |  / _ \  | '__| | __|  / _` | | |   | |\/| | | |    ");
        Logger.Info(@" |  __/  | (_) | | |    | |_  | (_| | | |   | |  | | | |___ ");
        Logger.Info(@" |_|      \___/  |_|     \__|  \__,_| |_|   |_|  |_|  \____|");
        Logger.Info("");

        ExceptionHandlerSetup.Register();

        if (args is ["--memory-optimize"])
        {
            Environment.Exit(MemoryOptimizationService.OptimizeCurrentProcessContext());
            return;
        }

        DebugConsole.ShowIfEnabled();

        if (!SingleInstanceGuard.TryAcquire())
        {
            SingleInstanceGuard.HandleSecondaryLaunch(args);
            return;
        }

        if (!PrimaryInstanceStartup.Run(args))
            return;

        try
        {
            BuildAvaloniaApp(args)
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Logger.Fatal(ex);
            throw;
        }
    }

    private static AppBuilder BuildAvaloniaApp(string[] args)
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if WINDOWS
            .WithWindowsJumpList(args)
#endif
#if DEBUG
            .UseHotReload()
#endif
            .WithManagedSystemDialogs()
            .WithInterFont()
            .LogToTrace();
}
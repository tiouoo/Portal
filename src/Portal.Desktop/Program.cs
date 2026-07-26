using System;
using System.Runtime.InteropServices;
using Avalonia;
#if DEBUG
using HotAvalonia;
#endif
using Portal.Core.Minecraft;
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
        // 命令行 / portal:// 命令：能转发给已运行实例（或只需输出帮助）就直接退出，
        // 否则命令已入队，继续正常启动并在 UI 加载后执行。
        if (PortalCommandService.TryHandleStartupArgs(args))
            return;

#if WINDOWS
        if (WindowsJumpListService.TryForwardToRunningInstance(args))
            return;

        WindowsJumpListService.StartCommandServer();
        WindowsJumpListService.SetAppUserModelId();
#endif
        PortalCommandService.StartCommandServer();
        PortalCommandService.Initialize();
        Initializer.Program("Portal", "xyz.tiouo.Portal");
        Logger.Info("应用程序启动 Main()");

#if WINDOWS
        RegisterBedrockLauncher();
#endif
        
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Logger.Info("Running on Windows");
        else if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Logger.Info("Running on Linux");
        else if(RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Logger.Info("Running on macOS");
        
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

#if WINDOWS
    private static void RegisterBedrockLauncher()
    {
        MinecraftLaunchService.DefaultBedrockLauncherFactory =
            config => new Portal.Bedrock.BedrockLaunch(config);
        Portal.Bedrock.Standard.Interface.BedrockInstallationService.DefaultInstaller =
            new Portal.Bedrock.BedrockInstaller();
    }
#endif

    // Avalonia configuration, don't remove; also used by visual designer.
    private static AppBuilder BuildAvaloniaApp(string[] args)
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if WINDOWS
            .WithWindowsJumpList(args)
#endif
#if DEBUG
            .UseHotReload()
            // .WithDeveloperTools()
#endif
            .WithManagedSystemDialogs()
            .WithInterFont()
            .LogToTrace();
}

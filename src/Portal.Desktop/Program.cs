using System;
using System.Runtime.InteropServices;
using Avalonia;
#if DEBUG
using HotAvalonia;
#endif
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Services;
using Tio.Avalonia.Standard.Modules;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Velopack;

namespace Portal.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            .SetArgs(args)
            .SetAutoApplyOnStartup(false)
            .Run();

        if (TryGetBedrockPackagePath(args, out var packagePath))
            App.BedrockPackagePath = packagePath;

        // 命令行 / portal:// 命令：能转发给已运行实例（或只需输出帮助）就直接退出，
        // 否则命令已入队，继续正常启动并在 UI 加载后执行。
        if (packagePath == null && PortalCommandService.TryHandleStartupArgs(args))
            return;

#if WINDOWS
        if (packagePath == null && WindowsJumpListService.TryForwardToRunningInstance(args))
            return;

        if (packagePath == null)
            WindowsJumpListService.StartCommandServer();
        WindowsJumpListService.SetAppUserModelId();
#endif
        if (packagePath == null)
        {
            PortalCommandService.StartCommandServer();
            PortalCommandService.Initialize();
        }
        var versionInfo = Module.Initialize.Config.LoadVersionInfo();
        Initializer.Program("Portal", "xyz.tiouo.Portal", versionInfo.VersionTitle);
#if WINDOWS
        WindowsBedrockFileAssociationService.Register();
#endif
        Logger.Info("Portal MC");
        Logger.Info("  ____                   _             _     __  __    ____ ");
        Logger.Info(" |  _ \\    ___    _ __  | |_    __ _  | |   |  \\/  |  / ___|");
        Logger.Info(" | |_) |  / _ \\  | '__| | __|  / _` | | |   | |\\/| | | |    ");
        Logger.Info(" |  __/  | (_) | | |    | |_  | (_| | | |   | |  | | | |___ ");
        Logger.Info(" |_|      \\___/  |_|     \\__|  \\__,_| |_|   |_|  |_|  \\____|");
        Logger.Info(">");
        Logger.Info("应用程序启动 Main()");

#if WINDOWS
        RegisterBedrockLauncher();
#endif

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Logger.Info("Running on Windows");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Logger.Info("Running on Linux");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
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

    private static bool TryGetBedrockPackagePath(string[] args, out string? packagePath)
    {
        packagePath = null;
        if (args.Length != 1 || !BedrockPackageImportService.TryGetArchiveType(args[0], out _))
            return false;

        var fullPath = Path.GetFullPath(args[0]);
        if (!File.Exists(fullPath))
            return false;

        packagePath = fullPath;
        return true;
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

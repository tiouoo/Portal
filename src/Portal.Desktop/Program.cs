using System;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Avalonia;
#if DEBUG
using HotAvalonia;
#endif
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Services;
using Portal.Core.SystemResources;
using Portal.Module.Ipc;
using Tio.Avalonia.Standard.Modules;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Desktop;

sealed class Program
{
    private static int _isLoggingFirstChanceException;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.FirstChanceException += (_, eventArgs) =>
        {
            // Logger itself can encounter I/O exceptions; avoid recursively logging those exceptions.
            if (Interlocked.Exchange(ref _isLoggingFirstChanceException, 1) != 0) return;
            try
            {
                Logger.Debug($"运行时引发异常（可能随后被业务代码处理）。{Environment.NewLine}{eventArgs.Exception}");
            }
            finally
            {
                Volatile.Write(ref _isLoggingFirstChanceException, 0);
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
                Logger.Fatal("未处理的 AppDomain 异常。", exception);
            else
                Logger.Fatal($"未处理的 AppDomain 非异常对象：{eventArgs.ExceptionObject}");
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Logger.Error("检测到未观察的任务异常。", eventArgs.Exception);
            eventArgs.SetObserved();
        };

        if (args.Length == 1 && args[0] == "--memory-optimize")
        {
            Environment.Exit(MemoryOptimizationService.OptimizeCurrentProcessContext());
            return;
        }

        DebugConsole.ShowIfEnabled();

        // AppImage does not reliably leave a permanent desktop entry behind. Keep its
        // portal:// handler registered before processing the incoming protocol argument.
        ProtocolRegistration.TryRegisterLinuxOnStartupAsync().GetAwaiter().GetResult();

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
        Logger.Info($"开始启动应用，命令行参数数量：{args.Length}");
        var versionInfo = Module.Initialize.Config.LoadVersionInfo();
        Initializer.Program("Portal", "cc.tiouo.Portal", versionInfo.VersionTitle);
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

#if WINDOWS || LINUX
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
        if (args.Length != 1)
            return false;

        var path = Uri.TryCreate(args[0], UriKind.Absolute, out var uri) && uri.IsFile
            ? uri.LocalPath
            : args[0];
        if (!BedrockPackageImportService.TryGetArchiveType(path, out _))
            return false;

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            return false;

        packagePath = fullPath;
        return true;
    }

#if WINDOWS || LINUX
    private static void RegisterBedrockLauncher()
    {
#if WINDOWS
        MinecraftLaunchService.DefaultBedrockLauncherFactory =
            config => new Portal.Bedrock.BedrockLaunch(config);
        Portal.Bedrock.Standard.Interface.BedrockInstallationService.DefaultInstaller =
            new Portal.Bedrock.BedrockInstaller();
        Portal.Bedrock.Standard.Interface.BedrockToolsService.Default =
            new Portal.Bedrock.BedrockWindowsToolsService();
#elif LINUX
        MinecraftLaunchService.DefaultBedrockLauncherFactory =
            config => new Portal.Bedrock.Linux.BedrockLaunch(config);
        Portal.Bedrock.Standard.Interface.BedrockInstallationService.DefaultInstaller =
            new Portal.Bedrock.Linux.BedrockInstaller();
#endif
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

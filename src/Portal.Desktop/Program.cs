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

    private const string SingleInstanceMutexName = "cc.tiouo.Portal.Singleton";

    /// <summary>主实例持有的单例互斥锁；进程存活期间必须保持引用，防止被 GC 提前释放。</summary>
    private static Mutex? _singleInstanceMutex;

    /// <summary>单例模式下，已运行实例监听的命名管道可能尚未就绪；重复尝试发送，避免启动竞态。</summary>
    private const int PortalForwardAttempts = 4;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        
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

        if (!TryAcquireSingleInstance())
        {
            HandleSecondaryLaunch(args);
            return;
        }
        PortalCommandQueue.Initialize();
        ProtocolRegistration.TryRegisterLinuxOnStartupAsync().GetAwaiter().GetResult();

        if (TryGetBedrockPackagePath(args, out var packagePath))
            App.BedrockPackagePath = packagePath;

        if (TryGetJavaPackagePath(args, out var javaPackagePath))
        {
            var javaCommand = new PortalCommand
            {
                Kind = PortalCommandKind.DownloadModpack,
                Source = javaPackagePath
            };

            if (packagePath == null && PortalCommandService.TryForwardToRunningInstance(javaCommand))
            {
                Logger.Info($"已将 Java 整合包命令转发给正在运行的 Portal 实例：{javaPackagePath}");
                return;
            }

            App.JavaPackagePath = javaPackagePath;
            if (packagePath == null)
                PortalCommandQueue.Enqueue(javaCommand);
        }

        if (packagePath == null && javaPackagePath == null && PortalCommandService.TryHandleStartupArgs(args))
            return;

#if WINDOWS
        if (packagePath == null && javaPackagePath == null && WindowsJumpListService.TryForwardToRunningInstance(args))
            return;

        if (packagePath == null)
            WindowsJumpListService.StartCommandServer();
        WindowsJumpListService.SetAppUserModelId();
#endif
        if (packagePath == null)
            PortalCommandService.StartCommandServer();
        Logger.Info($"开始启动应用，命令行参数数量：{args.Length}");
        var versionInfo = Module.Initialize.Config.LoadVersionInfo();
        Initializer.Program("Portal", "cc.tiouo.Portal", versionInfo.VersionTitle);
#if WINDOWS
        WindowsBedrockFileAssociationService.Register();
        WindowsJavaFileAssociationService.Register();
#endif
        Logger.Info("Portal MC");
        Logger.Info("  ____                   _             _     __  __    ____ ");
        Logger.Info(" |  _ \\    ___    _ __  | |_    __ _  | |   |  \\/  |  / ___|");
        Logger.Info(" | |_) |  / _ \\  | '__| | __|  / _` | | |   | |\\/| | | |    ");
        Logger.Info(" |  __/  | (_) | | |    | |_  | (_| | | |   | |  | | | |___ ");
        Logger.Info(" |_|      \\___/  |_|     \\__|  \\__,_| |_|   |_|  |_|  \\____|");
        Logger.Info("");
        Logger.Info("应用程序启动 Main()");

#if WINDOWS || LINUX
        RegisterBedrockLauncher();
#endif

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Logger.Info("操作系统：Windows");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Logger.Info("操作系统：Linux");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Logger.Info("操作系统：macOS");

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

    /// <summary>
    /// 获取单例互斥锁。返回 true 表示本进程是唯一实例，应继续正常启动；
    /// 返回 false 表示已有实例在运行，应只转发参数/请求显示窗口后退出。
    /// </summary>
    private static bool TryAcquireSingleInstance()
    {
        // initiallyOwned: true —— 创建成功时本进程（创建线程）即拥有互斥锁，持有到进程退出。
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (createdNew)
        {
            Logger.Info("已获取单例互斥锁，本进程为唯一实例。");
            return true;
        }

        try
        {
            // 互斥锁已存在：尝试立刻获取。若上一个实例已崩溃退出（abandoned），则接管成为主实例。
            if (_singleInstanceMutex.WaitOne(0))
            {
                Logger.Warning("检测到已停用（abandoned）的单例互斥锁，本进程接管为唯一实例。");
                return true;
            }
        }
        catch (AbandonedMutexException)
        {
            Logger.Warning("检测到已停用（abandoned）的单例互斥锁，本进程接管为唯一实例。");
            return true;
        }

        Logger.Info("检测到 Portal 正在运行，本进程将只转发外部命令或请求显示窗口后退出。");
        return false;
    }

    /// <summary>
    /// 已有 Portal 实例在运行时的本进程行为：
    /// 把命令行参数（安装/启动命令、整合包路径、Jump List 等）转发给主实例，
    /// 普通启动则让主实例显示并激活窗口，然后退出本进程，保证只运行一个实例。
    /// </summary>
    private static void HandleSecondaryLaunch(string[] args)
    {
        if (TryGetBedrockPackagePath(args, out var bedrockPath))
        {
            ForwardSecondaryCommand(new PortalCommand { Kind = PortalCommandKind.DownloadModpack, Source = bedrockPath });
            return;
        }

        if (TryGetJavaPackagePath(args, out var javaPath))
        {
            ForwardSecondaryCommand(new PortalCommand { Kind = PortalCommandKind.DownloadModpack, Source = javaPath });
            return;
        }

#if WINDOWS
        if (WindowsJumpListService.TryForwardToRunningInstance(args))
            return;
#endif

        switch (PortalCommandParser.Parse(args, out var command, out var error))
        {
            case PortalCliParseStatus.Help:
                PortalCommandService.WriteConsole(PortalCommandParser.GetUsageText());
                return;
            case PortalCliParseStatus.Error:
                PortalCommandService.WriteConsole($"参数错误：{error}{Environment.NewLine}{Environment.NewLine}{PortalCommandParser.GetUsageText()}");
                return;
            case PortalCliParseStatus.Command when command is not null:
                ForwardSecondaryCommand(command);
                return;
            default:
                NotifySecondaryShowMainWindow();
                return;
        }
    }

    private static void ForwardSecondaryCommand(PortalCommand command)
    {
        if (PortalCommandService.TryForwardToRunningInstance(command, PortalForwardAttempts))
        {
            PortalCommandService.WriteConsole("已将命令转发给正在运行的 Portal 实例。");
            Logger.Info($"已将命令转发给正在运行的 Portal 实例：{command.Kind}");
        }
        else
        {
            Logger.Warning("检测到 Portal 已在运行，但命令转发失败，本进程退出。");
        }
    }

    private static void NotifySecondaryShowMainWindow()
    {
        var showCommand = new PortalCommand { Kind = PortalCommandKind.ShowMainWindow };
        if (PortalCommandService.TryForwardToRunningInstance(showCommand, PortalForwardAttempts))
            Logger.Info("已通知正在运行的 Portal 实例显示主窗口。");
        else
            Logger.Warning("检测到 Portal 已在运行，但无法通知其显示主窗口。");
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

    private static bool TryGetJavaPackagePath(string[] args, out string? packagePath)
    {
        packagePath = null;
        if (args.Length != 1)
            return false;

        var path = Uri.TryCreate(args[0], UriKind.Absolute, out var uri) && uri.IsFile
            ? uri.LocalPath
            : args[0];

        var extension = Path.GetExtension(path);
        if (!string.Equals(extension, ".mrpack", StringComparison.OrdinalIgnoreCase))
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

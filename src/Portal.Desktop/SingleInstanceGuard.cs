using System.Threading;
using Portal.Core.Module.Ipc;
using Portal.Module.Ipc;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Desktop;

internal static class SingleInstanceGuard
{
    private const string MutexName = "cc.tiouo.Portal.Singleton";
    private static Mutex? _mutex;

    private const int ForwardAttempts = 4;
    
    public static bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (createdNew)
        {
            Logger.Info("已获取单例互斥锁，本进程为唯一实例。");
            return true;
        }

        try
        {
            if (_mutex.WaitOne(0))
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
    
    public static void HandleSecondaryLaunch(string[] args)
    {
        if (PackagePathResolver.TryGetBedrockPackagePath(args, out var bedrockPath))
        {
            ForwardCommand(new PortalCommand { Kind = PortalCommandKind.DownloadModpack, Source = bedrockPath });
            return;
        }

        if (PackagePathResolver.TryGetJavaPackagePath(args, out var javaPath))
        {
            ForwardCommand(new PortalCommand { Kind = PortalCommandKind.DownloadModpack, Source = javaPath });
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
                ForwardCommand(command);
                return;
            case PortalCliParseStatus.NotACommand:
            default:
                NotifyShowMainWindow();
                return;
        }
    }

    private static void ForwardCommand(PortalCommand command)
    {
        if (PortalCommandService.TryForwardToRunningInstance(command, ForwardAttempts))
        {
            PortalCommandService.WriteConsole("已将命令转发给正在运行的 Portal 实例。");
            Logger.Info($"已将命令转发给正在运行的 Portal 实例：{command.Kind}");
        }
        else
        {
            Logger.Warning("检测到 Portal 已在运行，但命令转发失败，本进程退出。");
        }
    }

    private static void NotifyShowMainWindow()
    {
        var showCommand = new PortalCommand { Kind = PortalCommandKind.ShowMainWindow };
        if (PortalCommandService.TryForwardToRunningInstance(showCommand, ForwardAttempts))
            Logger.Info("已通知正在运行的 Portal 实例显示主窗口。");
        else
            Logger.Warning("检测到 Portal 已在运行，但无法通知其显示主窗口。");
    }
}

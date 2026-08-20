using Portal.Core.Module.Ipc;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Desktop;

internal static class SingleInstanceGuard
{
    private const string MutexName = "cc.tiouo.Portal.Singleton";

    private const int ForwardAttempts = 4;
    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (createdNew)
        {
            Logger.Info(LogLanguageManager.Instance.desktop_singleInstance_acquired.CurrentValue());
            return true;
        }

        try
        {
            if (_mutex.WaitOne(0))
            {
                Logger.Warning(LogLanguageManager.Instance.desktop_singleInstance_abandoned.CurrentValue());
                return true;
            }
        }
        catch (AbandonedMutexException)
        {
            Logger.Warning(LogLanguageManager.Instance.desktop_singleInstance_abandoned.CurrentValue());
            return true;
        }

        Logger.Info(LogLanguageManager.Instance.desktop_singleInstance_runningDetected.CurrentValue());
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
                PortalCommandService.WriteConsole(PortalCommandParser.GetHeadlessUsageText());
                return;
            case PortalCliParseStatus.Error:
                PortalCommandService.WriteConsole(
                    string.Format(CommonLanguageManager.Instance.desktop_commandService_argumentError.CurrentValue(), error, Environment.NewLine, Environment.NewLine, PortalCommandParser.GetUsageText()));
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
            PortalCommandService.WriteConsole(CommonLanguageManager.Instance.desktop_commandService_forwarded.CurrentValue());
            Logger.Info(string.Format(LogLanguageManager.Instance.desktop_singleInstance_forwardedWithKind.CurrentValue(), command.Kind));
        }
        else
        {
            Logger.Warning(LogLanguageManager.Instance.desktop_singleInstance_forwardFailed.CurrentValue());
        }
    }

    private static void NotifyShowMainWindow()
    {
        var showCommand = new PortalCommand { Kind = PortalCommandKind.ShowMainWindow };
        if (PortalCommandService.TryForwardToRunningInstance(showCommand, ForwardAttempts))
            Logger.Info(LogLanguageManager.Instance.desktop_singleInstance_notifyShowWindow.CurrentValue());
        else
            Logger.Warning(LogLanguageManager.Instance.desktop_singleInstance_notifyShowWindowFailed.CurrentValue());
    }
}
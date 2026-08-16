using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Desktop;

internal static class ExceptionHandlerSetup
{
    private static int _isLoggingFirstChanceException;

    public static void Register()
    {
        AppDomain.CurrentDomain.FirstChanceException += (_, eventArgs) =>
        {
            if (Interlocked.Exchange(ref _isLoggingFirstChanceException, 1) != 0) return;
            try
            {
                Logger.Debug($"运行时引发异常：{Environment.NewLine}{eventArgs.Exception}");
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
    }
}
using Portal.Localization;
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
                Logger.Debug(string.Format(LogLanguageManager.Instance.desktop_exceptionHandler_runtimeException.CurrentValue(), Environment.NewLine, eventArgs.Exception));
            }
            finally
            {
                Volatile.Write(ref _isLoggingFirstChanceException, 0);
            }
        };

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
                Logger.Fatal(LogLanguageManager.Instance.desktop_exceptionHandler_unhandledAppDomain.CurrentValue(), exception);
            else
                Logger.Fatal(string.Format(LogLanguageManager.Instance.desktop_exceptionHandler_unhandledAppDomainNonException.CurrentValue(), eventArgs.ExceptionObject));
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Logger.Error(LogLanguageManager.Instance.desktop_exceptionHandler_unobservedTaskException.CurrentValue(), eventArgs.Exception);
            eventArgs.SetObserved();
        };
    }
}
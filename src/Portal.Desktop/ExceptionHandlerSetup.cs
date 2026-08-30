using Portal.Localization;
using Portal.Core.Services;
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
            {
                Logger.Fatal(LogLanguageManager.Instance.desktop_exceptionHandler_unhandledAppDomain.CurrentValue(), exception);
                SendCrashTelemetry(exception, eventArgs.IsTerminating);
            }
            else
            {
                Logger.Fatal(string.Format(LogLanguageManager.Instance.desktop_exceptionHandler_unhandledAppDomainNonException.CurrentValue(), eventArgs.ExceptionObject));
                SendCrashTelemetry(null, eventArgs.IsTerminating);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Logger.Error(LogLanguageManager.Instance.desktop_exceptionHandler_unobservedTaskException.CurrentValue(), eventArgs.Exception);
            eventArgs.SetObserved();
        };
    }

    private static void SendCrashTelemetry(Exception? exception, bool isTerminating)
    {
        if (!isTerminating)
            return;

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            // The process is terminating for AppDomain.UnhandledException; block briefly so
            // the best-effort crash event can leave the process before it exits.
            TelemetryService.SendCrashAsync(exception, isTerminating, cancellation.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Logger.Warning(LogLanguageManager.Instance.telemetry_timedOut.CurrentValue());
        }
        catch
        {
            Logger.Warning(LogLanguageManager.Instance.telemetry_failed.CurrentValue());
        }
    }
}

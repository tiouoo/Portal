using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Portal.Core.Const;
using Portal.Core.Module.Initialize;
using Portal.Core.Module.Ipc;
using Portal.Localization;
using Portal.Module;
using Portal.Module.Initialize;
using Portal.Views;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal;

public partial class App : Application
{
    public delegate void UiLoadedEventHandler(TabWindow ui);

    private TabWindow _win;
    public static long StartupTimestamp;
    public static string? BedrockPackagePath { get; set; }
    public static string? JavaPackagePath { get; set; }

    public static TabWindow? MainWindow => (Current!.ApplicationLifetime
        as IClassicDesktopStyleApplicationLifetime).MainWindow as TabWindow;

    public static TopLevel TopLevel => TopLevel.GetTopLevel(MainWindow);
    public static event UiLoadedEventHandler? UiLoaded;

    public override void Initialize()
    {
        Logger.Info(LogLanguageManager.Instance.app_initStart.CurrentValue());
        if (BedrockPackagePath == null)
            Initializer.App();
        else
            Initializer.BedrockPackageImport();
        AvaloniaXamlLoader.Load(this);
        Logger.Info(LogLanguageManager.Instance.app_initComplete.CurrentValue());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Logger.Info("OnFrameworkInitializationCompleted");
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
#if DEBUG
            Logger.Debug("挂载 Devtools");
            this.AttachDeveloperTools();
#else
            Logger.Info("注册全局异常处理");
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            Dispatcher.UIThread.UnhandledException += UIThread_UnhandledException;
#endif
            if (BedrockPackagePath is { } packagePath)
            {
                Initializer.Oobe();
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                var importWindow = new BedrockPackageImportWindow(packagePath);
                LogWindowShowElapsed(importWindow);
                desktop.MainWindow = importWindow;
            }
            else if (Data.ConfigEntry.IsInitialized)
            {
                Initializer.Oobe();
                ShowMainWindow(desktop);
            }
            else
            {
                Logger.Info("尚未完成初始化，进入初始化窗口");
                Initializer.Oobe();
                var oobe = new OobeWindow();
                LogWindowShowElapsed(oobe);
                desktop.MainWindow = oobe;
                oobe.Completed += () =>
                {
                    Logger.Info("初始化完成，进入主窗口");
                    Data.ConfigEntry.IsInitialized = true;
                    ConfigSaver.FlushConfig();
                    ShowMainWindow(desktop);
                    _win.Show();
                    oobe.Close();
                };
            }

            Logger.Info("UI配置完成");
        }

        if (BedrockPackagePath == null)
        {
            PortalCommandQueue.ExecutionHandler = PortalCommandExecutor.ExecuteAsync;
            PortalCommandQueue.Initialize();
        }

        if (BedrockPackagePath == null && this.TryGetFeature<IActivatableLifetime>() is { } activatableLifetime)
            activatableLifetime.Activated += OnActivated;

        base.OnFrameworkInitializationCompleted();
    }

    private void OnActivated(object? sender, ActivatedEventArgs e)
    {
        if (e is not ProtocolActivatedEventArgs { Uri: { } uri }) return;
        Logger.Info($"收到协议激活：{uri}");
        if (PortalCommandParser.Parse([uri.ToString()], out var command, out var error) ==
            PortalCliParseStatus.Command && command is not null)
            PortalCommandQueue.Enqueue(command);
        else if (error is not null)
            Logger.Error($"协议激活链接无效：{error}");
    }

    private void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _win = new TabWindow(true);
        TextOptions.SetTextRenderingMode(_win, TextRenderingMode.Antialias);
        TextOptions.SetTextHintingMode(_win, TextHintingMode.Light);
        TextOptions.SetBaselinePixelAlignment(_win, BaselinePixelAlignment.Aligned);

        LogWindowShowElapsed(_win);
        desktop.MainWindow = _win;
        _win.Loaded += Function;
    }

    private static void LogWindowShowElapsed(Window window)
    {
        window.Opened += (_, _) =>
            Logger.Info($"窗口显示完成，从程序启动到窗口显示耗时 {Stopwatch.GetElapsedTime(StartupTimestamp).TotalMilliseconds:F0} ms。");
    }

    private async void Function(object? sender, RoutedEventArgs e)
    {
        Logger.Info(LogLanguageManager.Instance.app_uiLoadComplete.CurrentValue());
        _win.Loaded -= Function;
        await Task.Yield();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await Initializer.UiAsync();
            Logger.Info(string.Format(LogLanguageManager.Instance.app_uiDataLoadComplete.CurrentValue(), stopwatch.ElapsedMilliseconds));
        }
        catch (Exception exception)
        {
            Logger.Error(LogLanguageManager.Instance.app_uiDataLoadFailed.CurrentValue(), exception);
        }
        finally
        {
            _win.IsUiLoading = false;
            PortalCommandQueue.MarkUiLoaded();
            UiLoaded?.Invoke(_win);
        }
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            Logger.Fatal("AppDomain 异常。", exception);
        else
            Logger.Fatal($"AppDomain 异常：{e.ExceptionObject}");
        try
        {
            var win = new CrashWindow(e.ToString() ?? "Unhandled Exception");
            win.Show();
        }
        catch (Exception ex)
        {
            Logger.Fatal("显示崩溃窗口失败。", ex);
        }
    }

    private void UIThread_UnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Fatal("UI线程异常。", e.Exception);
        try
        {
            var win = new CrashWindow(e.Exception.ToString());
            win.Show();
        }
        catch (Exception ex)
        {
            Logger.Fatal("显示崩溃窗口失败。", ex);
        }
        finally
        {
            e.Handled = true;
        }
    }
}
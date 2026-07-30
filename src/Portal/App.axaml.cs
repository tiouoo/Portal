using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Portal.Const;
using Portal.Module.Initialize;
using Portal.Module.Ipc;
using Portal.ViewModels;
using Portal.Views;
using Portal.Views.Pages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Entries;

namespace Portal;

public partial class App : Application
{
    public static string? BedrockPackagePath { get; set; }
    
    public delegate void UiLoadedEventHandler(TabWindow ui);

    private TabWindow _win;

    public static TabWindow? MainWindow => (Current!.ApplicationLifetime
        as IClassicDesktopStyleApplicationLifetime).MainWindow as TabWindow;

    public static TopLevel TopLevel => TopLevel.GetTopLevel(MainWindow);
    public static event UiLoadedEventHandler? UiLoaded;

    public override void Initialize()
    {
        Logger.Info("开始初始化");
        if (BedrockPackagePath == null)
            Initializer.App();
        else
            Initializer.BedrockPackageImport();
        AvaloniaXamlLoader.Load(this);
        Logger.Info("完成初始化");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Logger.Info("OnFrameworkInitializationCompleted");
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
#if DEBUG
            Logger.Debug("挂载 Devtools");
            this.AttachDeveloperTools();
#elif RELEASE
            Logger.Info("注册全局异常处理");
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            Dispatcher.UIThread.UnhandledException += UIThread_UnhandledException;
#endif
            if (BedrockPackagePath is { } packagePath)
            {
                Initializer.Oobe();
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                desktop.MainWindow = new BedrockPackageImportWindow(packagePath);
            }
            else if (Data.ConfigEntry.IsInitialized)
            {
                ShowMainWindow(desktop);
            }
            else
            {
                Logger.Info("尚未完成初始化，进入初始化窗口");
                Initializer.Oobe();
                var oobe = new OobeWindow();
                desktop.MainWindow = oobe;
                oobe.Completed += () =>
                {
                    Logger.Info("初始化完成，进入主窗口");
                    Data.ConfigEntry.IsInitialized = true;
                    Method.FlushConfig();
                    ShowMainWindow(desktop);
                    _win.Show();
                    oobe.Close();
                };
            }

            Logger.Info("UI配置完成");
        }

        // macOS 上 portal:// 链接经 Apple Event（协议激活）送达，而非命令行参数。
        if (BedrockPackagePath == null)
            PortalCommandQueue.Initialize();
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
        
        // TextOptions.SetTextRenderingMode(this, TextRenderingMode.Unspecified);
        // TextOptions.SetTextHintingMode(this, TextHintingMode.Unspecified);
        // TextOptions.SetBaselinePixelAlignment(this, BaselinePixelAlignment.Unspecified);
        
        desktop.MainWindow = _win;
        _win.Loaded += Function;
    }

    private async void Function(object? sender, RoutedEventArgs e)
    {
        Logger.Info("UI加载完成");
        _win.Loaded -= Function;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        try
        {
            await Initializer.UiAsync();
        }
        catch (Exception exception)
        {
            Logger.Error($"后台加载 UI 数据失败：{exception}");
        }
        finally
        {
            _win.IsUiLoading = false;
            UiLoaded?.Invoke(_win);
        }
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Logger.Fatal($"AppDomain 异常: {e}");
        try
        {
            var win = new CrashWindow(e.ToString() ?? "Unhandled Exception");
            win.Show();
        }
        catch (Exception ex)
        {
            Logger.Fatal($"显示崩溃窗口失败: {ex}");
        }
    }

    private void UIThread_UnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Fatal($"UI线程异常: {e.Exception}");
        try
        {
            var win = new CrashWindow(e.Exception.ToString());
            win.Show();
        }
        catch (Exception ex)
        {
            Logger.Fatal($"显示崩溃窗口失败: {ex}");
        }
        finally
        {
            e.Handled = true;
        }
    }

    private void OpenSetting_OnClick(object? sender, EventArgs e)
    {
        if (UiProperty.TabWindow is not { } window) return;
        var tabEntry = new TabEntry(window, new SettingPage());
        window.CreateTab(tabEntry);
        window.SelectTab(tabEntry);
        window.Activate();
    }
}

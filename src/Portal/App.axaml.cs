using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Portal.Const;
using Portal.Module.Initialize;
using Portal.ViewModels;
using Portal.Views;
using Portal.Views.Pages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Entries;

namespace Portal;

public partial class App : Application
{
    public delegate void UiLoadedEventHandler(TabWindow ui);

    private TabWindow _win;

    public static TabWindow? MainWindow => (Current!.ApplicationLifetime
        as IClassicDesktopStyleApplicationLifetime).MainWindow as TabWindow;

    public static TopLevel TopLevel => TopLevel.GetTopLevel(MainWindow);
    public static event UiLoadedEventHandler? UiLoaded;

    public override void Initialize()
    {
        Logger.Info("开始初始化");
        Initializer.App();
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
            _win = new TabWindow(true);
            desktop.MainWindow = _win;
            _win.Loaded += Function;
            Logger.Info("UI配置完成");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void Function(object? sender, RoutedEventArgs e)
    {
        Logger.Info("UI加载完成");
        Initializer.Ui();
        UiLoaded?.Invoke(_win);
        _win.Loaded -= Function;
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
        if(UiProperty.TabWindow is not { } window)  return;
        var tabEntry = new TabEntry(window, new SettingPage());
        window.CreateTab(tabEntry);
        window.SelectTab(tabEntry);
        window.Activate();
    }
}
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Portal.Classes.Enums;
using Portal.Const;
using Portal.Module.Initialize;

namespace Portal.Services;

/// <summary>
/// 根据「启动器可见性」设置，在游戏启动/退出时隐藏、最小化、恢复或退出启动器。
/// </summary>
public static class PortalVisibilityService
{
    private static int _runningGames;
    private static PortalVisibleMode _appliedMode = PortalVisibleMode.NoOperation;
    private static readonly List<Window> _hiddenWindows = [];
    private static readonly List<(Window Window, WindowState State)> _minimizedWindows = [];

    public static void OnGameStarted() => Dispatcher.UIThread.Post(() =>
    {
        _runningGames++;
        if (_runningGames > 1)
            return;

        _appliedMode = Data.ConfigEntry.PortalVisibleMode;
        switch (_appliedMode)
        {
            case PortalVisibleMode.QuitAfterLaunch:
                Shutdown();
                break;
            case PortalVisibleMode.HiddenAfterLaunchAndReopen:
                HideAllWindows();
                break;
            case PortalVisibleMode.MinimizedAfterLaunch:
            case PortalVisibleMode.MinimizedAfterLaunchAndRestore:
                MinimizeAllWindows();
                break;
        }
    });

    public static void OnGameExited() => Dispatcher.UIThread.Post(() =>
    {
        _runningGames = Math.Max(0, _runningGames - 1);
        if (_runningGames > 0)
            return;

        switch (_appliedMode)
        {
            case PortalVisibleMode.HiddenAfterLaunchAndReopen:
                ShowHiddenWindows();
                break;
            case PortalVisibleMode.MinimizedAfterLaunchAndRestore:
                RestoreMinimizedWindows();
                break;
            case PortalVisibleMode.MinimizedAfterLaunch:
                _minimizedWindows.Clear();
                break;
        }

        _appliedMode = PortalVisibleMode.NoOperation;
    });

    private static IEnumerable<Window> GetWindows() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows
            : [];

    private static void HideAllWindows()
    {
        _hiddenWindows.Clear();
        foreach (var window in GetWindows())
        {
            if (!window.IsVisible)
                continue;
            _hiddenWindows.Add(window);
            window.Hide();
        }
    }

    private static void ShowHiddenWindows()
    {
        foreach (var window in _hiddenWindows)
        {
            try
            {
                window.Show();
                window.Activate();
            }
            catch
            {
                // 窗口可能已在隐藏期间被关闭
            }
        }
        _hiddenWindows.Clear();
    }

    private static void MinimizeAllWindows()
    {
        _minimizedWindows.Clear();
        foreach (var window in GetWindows())
        {
            if (!window.IsVisible || window.WindowState == WindowState.Minimized)
                continue;
            _minimizedWindows.Add((window, window.WindowState));
            window.WindowState = WindowState.Minimized;
        }
    }

    private static void RestoreMinimizedWindows()
    {
        foreach (var (window, state) in _minimizedWindows)
        {
            try
            {
                window.WindowState = state;
                window.Activate();
            }
            catch
            {
                // 窗口可能已在最小化期间被关闭
            }
        }
        _minimizedWindows.Clear();
    }

    private static void Shutdown()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 退出前立即落盘配置，避免防抖中的保存丢失
            ConfigSaver.FlushConfig();
            desktop.Shutdown();
        }
    }
}

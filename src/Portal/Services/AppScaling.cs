using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Portal.Const;
using Portal.Views.SubWindows;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Controls;

namespace Portal.Services;

/// <summary>
/// 应用级缩放：在系统缩放基础上再叠加一个缩放系数（例如系统 200% + 应用 50% = 100%）。
/// Avalonia 12 已移除控件上的 LayoutTransform，只能通过 <see cref="LayoutTransformControl"/>
/// 包裹内容实现布局级缩放，跨平台即时生效。
/// 通过全局 Loaded 类处理器捕获所有窗口：TioWindow 整体缩放（标题栏、内容、通知/弹层宿主一起缩放），
/// 对话框等普通窗口缩放其内容；弹出层通过全局 Popup 样式开启 InheritsTransform 自动跟随缩放。
/// </summary>
public class AppScaling
{
    private static readonly Dictionary<Window, LayoutTransformControl> Wrapped = [];
    private static double _scale = 1.0;

    static AppScaling()
    {
        // 全局类处理器：不依赖样式与事件先后顺序，任何窗口加载时都会被自动缩放。
        Control.LoadedEvent.AddClassHandler<Control>(OnControlLoaded);
    }

    private static void OnControlLoaded(Control sender, RoutedEventArgs e)
    {
        if (sender is Window window)
            Wrap(window);
    }

    /// <summary>应用缩放系数变化，立即刷新所有已包裹的窗口（主窗口等会先主动包裹一次）。</summary>
    public static void ApplyScale(double scale)
    {
        _scale = scale;
        var transform = new ScaleTransform(scale, scale);

        // 兜底：主窗口/标签窗口不依赖任何钩子，调整比例时必定被包裹并立即生效。
        foreach (var window in TioTabWindowBase.AllWindows.ToArray())
            Wrap(window);

        foreach (var ltc in Wrapped.Values)
        {
            ltc.LayoutTransform = transform;
            ltc.InvalidateMeasure();
        }
    }

    private static void Wrap(Window window)
    {
        if (window is OverlayWindow) return;

        var scale = ReadConfigScale();
        if (Wrapped.TryGetValue(window, out var existing))
        {
            existing.LayoutTransform = new ScaleTransform(scale, scale);
            existing.InvalidateMeasure();
            return;
        }

        // TioWindow：整体缩放（标题栏、内容、OverlayDialogHost 通知/弹层等一起缩放）。
        if (window is TioWindow tioWindow && tioWindow.RootBorder is { } root)
        {
            // 模板结构：VisualLayerManager > Border > Panel > [PART_Root, Panel(对话框宿主), Resizer]。
            // 包裹外层 Panel，使对话框宿主（通知、Toast、内嵌弹层）与内容一起缩放。
            if (root.Parent is Panel outerPanel && outerPanel.Parent is Border outerBorder)
            {
                var ltc = new LayoutTransformControl { LayoutTransform = new ScaleTransform(scale, scale) };
                outerBorder.Child = ltc;
                ltc.Child = outerPanel;
                Register(window, ltc);
                return;
            }

            if (WrapRootChild(window, root, scale)) return;
        }

        // 对话框（CustomDialogWindow 等普通窗口）：缩放窗口内容。
        if (window.Content is Control content && content is not LayoutTransformControl)
        {
            var ltc = new LayoutTransformControl { LayoutTransform = new ScaleTransform(scale, scale) };
            window.Content = ltc;
            ltc.Child = content;
            Register(window, ltc);
        }
    }

    private static bool WrapRootChild(Window window, Border root, double scale)
    {
        if (root.Child is LayoutTransformControl current)
        {
            current.LayoutTransform = new ScaleTransform(scale, scale);
            current.InvalidateMeasure();
            Register(window, current);
            return true;
        }

        if (root.Child is not Control content) return false;

        var ltc = new LayoutTransformControl { LayoutTransform = new ScaleTransform(scale, scale) };
        root.Child = ltc;
        ltc.Child = content;
        Register(window, ltc);
        return true;
    }

    private static void Register(Window window, LayoutTransformControl ltc)
    {
        Wrapped[window] = ltc;
        window.Closed -= OnWindowClosed;
        window.Closed += OnWindowClosed;
        ltc.InvalidateMeasure();
        window.InvalidateMeasure();
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;
        window.Closed -= OnWindowClosed;
        Wrapped.Remove(window);
    }

    private static double ReadConfigScale()
    {
        try
        {
            return Data.ConfigEntry.AppScale;
        }
        catch
        {
            return 1.0;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Portal.Const;
using Portal.Core.Const;
using Portal.Views.SubWindows;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Controls;

namespace Portal.Services;

public class AppScaling
{
    private static readonly Dictionary<Window, LayoutTransformControl> Wrapped = [];
    private static double _scale = 1.0;

    static AppScaling()
    {
        
        Control.LoadedEvent.AddClassHandler<Control>(OnControlLoaded);
    }

    private static void OnControlLoaded(Control sender, RoutedEventArgs e)
    {
        if (sender is Window window)
            Wrap(window);
    }

        public static void ApplyScale(double scale)
    {
        _scale = scale;
        var transform = new ScaleTransform(scale, scale);

        
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

        
        if (window is TioWindow tioWindow && tioWindow.RootBorder is { } root)
        {
            
            
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
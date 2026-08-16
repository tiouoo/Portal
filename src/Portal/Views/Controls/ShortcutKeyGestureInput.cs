using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Styling;
using TioUi.Controls;

namespace Portal.Views.Controls;

public class ShortcutKeyGestureInput : KeyGestureInput
{
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        
        if (Theme is null && this.TryFindResource(typeof(KeyGestureInput), out var value) &&
            value is ControlTheme theme)
        {
            Theme = theme;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        
        if (e.Key == Key.Escape)
        {
            Gesture = null;
            e.Handled = true;
            return;
        }

        
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System)
        {
            e.Handled = true;
            return;
        }

        
        if (e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            return;
        }

        Gesture = new KeyGesture(e.Key, e.KeyModifiers);
        e.Handled = true;
    }
}

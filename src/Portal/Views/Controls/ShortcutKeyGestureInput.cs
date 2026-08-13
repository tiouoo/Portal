using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Styling;
using TioUi.Controls;

namespace Portal.Views.Controls;

/// <summary>
/// 快捷键输入框：选中后按住键盘即可录入组合键。
/// 按 Esc 清空；只接受包含修饰键（Ctrl/Shift/Alt/Win）的组合，避免劫持正常文本输入。
/// </summary>
public class ShortcutKeyGestureInput : KeyGestureInput
{
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Avalonia 的隐式主题查找不会沿基类回溯，子类需显式套用基类 KeyGestureInput 的主题
        if (Theme is null && this.TryFindResource(typeof(KeyGestureInput), out var value) &&
            value is ControlTheme theme)
        {
            Theme = theme;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // 按 Esc 清空当前快捷键
        if (e.Key == Key.Escape)
        {
            Gesture = null;
            e.Handled = true;
            return;
        }

        // 忽略单独按下的修饰键
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System)
        {
            e.Handled = true;
            return;
        }

        // 必须包含修饰键，避免在文本输入框聚焦时劫持普通按键
        if (e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            return;
        }

        Gesture = new KeyGesture(e.Key, e.KeyModifiers);
        e.Handled = true;
    }
}

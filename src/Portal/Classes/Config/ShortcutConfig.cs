using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Portal.Classes.Entries;

/// <summary>
/// 快捷键配置（持久化）。键为 <see cref="ShortcutAction"/> 的名称，值为快捷键字符串（空串表示未设置/禁用）。
/// </summary>
public partial class ShortcutConfig : ObservableObject
{
    [ObservableProperty] public partial Dictionary<string, string> Bindings { get; set; } = ShortcutActions.CreateDefaultBindings();

    public string? GetGesture(ShortcutAction action) =>
        Bindings.TryGetValue(action.ToString(), out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>
    /// 设置某个操作的快捷键；传入 null / 空串表示清空。若与其他操作冲突，会先清除旧操作上的绑定。
    /// </summary>
    public void SetGesture(ShortcutAction action, string? gesture)
    {
        var value = string.IsNullOrWhiteSpace(gesture) ? string.Empty : gesture.Trim();
        if (value.Length > 0)
        {
            var actionKey = action.ToString();
            foreach (var key in Bindings.Where(pair => pair.Key != actionKey && pair.Value == value)
                         .Select(pair => pair.Key).ToList())
            {
                Bindings[key] = string.Empty;
            }
        }

        Bindings[action.ToString()] = value;
        OnPropertyChanged(nameof(Bindings));
    }

    /// <summary>把单个操作恢复为其默认快捷键（无默认则清空）。</summary>
    public void ResetAction(ShortcutAction action)
    {
        Bindings[action.ToString()] = ShortcutActions.GetDefaultGesture(action) ?? string.Empty;
        OnPropertyChanged(nameof(Bindings));
    }
}

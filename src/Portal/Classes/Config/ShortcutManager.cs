using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Portal.Const;
using Portal.Views;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Classes.Entries;

/// <summary>
/// 根据 <see cref="Data.ConfigEntry.Shortcuts"/> 配置，为所有 <see cref="TioTabWindowBase"/> 窗口注册快捷键。
/// 配置变化时自动重新应用。
/// </summary>
public static class ShortcutManager
{
    private static readonly Dictionary<TioTabWindowBase, List<KeyBinding>> Managed = new();
    private static readonly HashSet<TioTabWindowBase> ClosedSubscribed = new();
    private static bool _initialized;

    /// <summary>订阅配置变化。需在配置加载完成后调用一次。</summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        Data.ConfigEntry.Shortcuts.PropertyChanged += OnShortcutConfigChanged;
        ApplyToAll();
    }

    /// <summary>按当前配置重新应用窗口上的快捷键（先清除上一次管理的绑定）。</summary>
    public static void Apply(TioTabWindowBase window)
    {
        if (ClosedSubscribed.Add(window))
            window.Closed += OnWindowClosed;

        if (Managed.Remove(window, out var existing))
        {
            foreach (var binding in existing)
                window.KeyBindings.Remove(binding);
        }

        var config = Data.ConfigEntry.Shortcuts;
        if (config is null) return;

        var bindings = new List<KeyBinding>();
        foreach (var definition in ShortcutActions.All)
        {
            if (!definition.IsAvailable) continue;

            var gestureText = config.GetGesture(definition.Action);
            if (string.IsNullOrWhiteSpace(gestureText)) continue;

            try
            {
                var gesture = KeyGesture.Parse(gestureText);
                var binding = new KeyBinding
                {
                    Gesture = gesture,
                    Command = new RelayCommand(() =>
                    {
                        if (window is TabWindow tabWindow)
                            definition.Execute(tabWindow);
                    })
                };
                window.KeyBindings.Add(binding);
                bindings.Add(binding);
            }
            catch (Exception exception)
            {
                Logger.Warning($"快捷键「{definition.DisplayName}」配置无效（{gestureText}）：{exception.Message}");
            }
        }

        if (bindings.Count > 0)
            Managed[window] = bindings;
    }

    public static void ApplyToAll()
    {
        foreach (var window in TioTabWindowBase.AllWindows.ToArray())
            Apply(window);
    }

    private static void OnShortcutConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShortcutConfig.Bindings))
            ApplyToAll();
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not TioTabWindowBase window) return;
        Managed.Remove(window);
        ClosedSubscribed.Remove(window);
    }
}

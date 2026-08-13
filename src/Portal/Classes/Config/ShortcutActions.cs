using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Portal.Const;
using Portal.Module.Multiplayer;
using Portal.Views;
using Portal.Views.Pages;
using Portal.Views.Pages.DownloadPages;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Shared;

namespace Portal.Classes.Entries;

/// <summary>一条可配置的快捷键操作定义。</summary>
public sealed record ShortcutActionDefinition(
    ShortcutAction Action,
    string Category,
    string DisplayName,
    string? DefaultGesture,
    Action<TabWindow> Execute,
    Func<bool>? Available = null)
{
    /// <summary>当前平台是否支持该操作（例如基岩版联机仅限 Windows）。</summary>
    public bool IsAvailable => Available?.Invoke() ?? true;
}

/// <summary>设置页中的分组。</summary>
public sealed record ShortcutCategory(string Name, IReadOnlyList<ShortcutActionDefinition> Items);

/// <summary>
/// 快捷键操作目录：集中描述所有可绑定的操作、默认键位与执行逻辑。
/// </summary>
public static class ShortcutActions
{
    public static IReadOnlyList<ShortcutActionDefinition> All { get; } = Build();

    public static IReadOnlyList<string> CategoryOrder { get; } =
    [
        "标签页",
        "页面",
        "对话框",
        "应用"
    ];

    public static IReadOnlyList<ShortcutCategory> Categories { get; } = CategoryOrder
        .Select(name => new ShortcutCategory(name, All
            .Where(definition => definition.Category == name && definition.IsAvailable).ToList()))
        .Where(category => category.Items.Count > 0)
        .ToList();

    public static Dictionary<string, string> CreateDefaultBindings() => All
        .Where(definition => !string.IsNullOrWhiteSpace(definition.DefaultGesture))
        .ToDictionary(definition => definition.Action.ToString(), definition => definition.DefaultGesture!);

    public static string GetDisplayName(ShortcutAction action) =>
        All.First(definition => definition.Action == action).DisplayName;

    public static string? GetDefaultGesture(ShortcutAction action) =>
        All.First(definition => definition.Action == action).DefaultGesture;

    /// <summary>从配置中读取某个操作当前保存的快捷键字符串。</summary>
    public static string? GetStoredGesture(ShortcutAction action) =>
        Data.ConfigEntry.Shortcuts.GetGesture(action);

    /// <summary>把配置中的快捷键字符串解析为 <see cref="KeyGesture"/>；空或无效时返回 null。</summary>
    public static KeyGesture? ParseGesture(string? gestureText)
    {
        if (string.IsNullOrWhiteSpace(gestureText)) return null;
        try
        {
            return KeyGesture.Parse(gestureText);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>把 <see cref="KeyGesture"/> 转换为可持久化的字符串。</summary>
    public static string GestureToString(KeyGesture gesture) => gesture.ToString("g", null);

    private static List<ShortcutActionDefinition> Build()
    {
        var list = new List<ShortcutActionDefinition>
        {
            Define(ShortcutAction.NewTab, "标签页", "新建标签页", "Ctrl+T", window => window.CreateNewTabFunc?.Invoke()),
            Define(ShortcutAction.CloseTab, "标签页", "关闭当前标签页", "Ctrl+W", window => window.SelectedTab?.Close()),
            Define(ShortcutAction.CloseOtherTabs, "标签页", "关闭其他标签页", null, window => window.SelectedTab?.CloseOther()),
            Define(ShortcutAction.CloseAllTabs, "标签页", "关闭所有标签页", "Ctrl+Shift+W", window => window.CloseAllTab()),
            Define(ShortcutAction.OpenInNewWindow, "标签页", "在新窗口打开", null, window => window.SelectedTab?.MoveTabToNewWindow()),
            Define(ShortcutAction.NextTab, "标签页", "切换到下一个标签页", null, window =>
            {
                var tabs = window.Tabs;
                if (tabs.Count < 2 || window.SelectedTab is null) return;
                window.SelectTab(tabs[(tabs.IndexOf(window.SelectedTab) + 1) % tabs.Count]);
            }),
            Define(ShortcutAction.PreviousTab, "标签页", "切换到上一个标签页", null, window =>
            {
                var tabs = window.Tabs;
                if (tabs.Count < 2 || window.SelectedTab is null) return;
                window.SelectTab(tabs[(tabs.IndexOf(window.SelectedTab) - 1 + tabs.Count) % tabs.Count]);
            }),
            Define(ShortcutAction.MoveTabForward, "标签页", "标签页向前移动", null, window => window.SelectedTab?.MoveTabForward()),
            Define(ShortcutAction.MoveTabBackward, "标签页", "标签页向后移动", null, window => window.SelectedTab?.MoveTabBackward()),

            Define(ShortcutAction.OpenStartPage, "页面", "打开起始页", null, window => window.OpenPage(new StartPage())),
            Define(ShortcutAction.OpenNewTabPage, "页面", "打开新标签页", null, window => window.OpenPage(new NewTabPage())),
            Define(ShortcutAction.OpenWidgetsPage, "页面", "打开小组件", null, window => window.OpenPage(new WidgetsPage())),
            Define(ShortcutAction.OpenDownloadPage, "页面", "打开下载中心", null, window => window.OpenPage(new DownloadPage())),
            Define(ShortcutAction.OpenInstancesPage, "页面", "打开实例", null, window => window.OpenPage(new InstancesPage())),
            Define(ShortcutAction.OpenMultiplayerJava, "页面", "打开联机（Java）", null, window => window.OpenPage(new MultiplayerPage(MinecraftEdition.Java))),
            Define(ShortcutAction.OpenMultiplayerBedrock, "页面", "打开联机（基岩）", null, window => window.OpenPage(new MultiplayerPage(MinecraftEdition.Bedrock)),
                available: () => OperatingSystem.IsWindows()),
            Define(ShortcutAction.OpenNewsPage, "页面", "打开新闻", null, window => window.OpenPage(new NewsPage())),
            Define(ShortcutAction.OpenToolsPage, "页面", "打开实用工具", null, window => window.OpenPage(new ToolsPage())),
            Define(ShortcutAction.OpenTaskPage, "页面", "打开任务", null, window => window.OpenPage(new TaskPage())),
            Define(ShortcutAction.OpenSettingsPage, "页面", "打开设置", null, window => window.OpenPage(new SettingPage())),

            Define(ShortcutAction.OpenAggregatedSearch, "对话框", "打开聚合搜索", "Shift+S",
                window => window.OpenAggregatedSearchDialog()),
            Define(ShortcutAction.OpenCreateInstanceDialog, "对话框", "创建新实例", null,
                window => window.OpenCreateInstanceDialog()),
            Define(ShortcutAction.AddMinecraftFolder, "对话框", "添加文件夹（游戏目录）", null,
                window => window.OpenAddMinecraftFolderDialog()),

            Define(ShortcutAction.ToggleTheme, "应用", "切换主题", "Ctrl+Shift+Q", _ =>
            {
                Data.ConfigEntry.Theme = Data.ConfigEntry.Theme switch
                {
                    Theme.Light => Theme.Dark,
                    Theme.Dark => Theme.Mirage,
                    _ => Theme.Light
                };
            }),
#if DEBUG
            Define(ShortcutAction.OpenDebugPage, "应用", "打开调试页面", "Shift+F12", window => window.OpenDebugPage()),
#endif
            Define(ShortcutAction.RestartApp, "应用", "重启应用", null, _ => App.Method.RestartApp()),
            Define(ShortcutAction.ExitApp, "应用", "退出应用", null, _ => App.Method.TryExitApp()),
            Define(ShortcutAction.MinimizeWindow, "应用", "最小化窗口", null,
                window => window.WindowState = WindowState.Minimized)
        };

        // 切换到第 1~9 个标签页
        ShortcutAction[] selectTabActions =
        [
            ShortcutAction.SelectTab1, ShortcutAction.SelectTab2, ShortcutAction.SelectTab3,
            ShortcutAction.SelectTab4, ShortcutAction.SelectTab5, ShortcutAction.SelectTab6,
            ShortcutAction.SelectTab7, ShortcutAction.SelectTab8, ShortcutAction.SelectTab9
        ];
        for (var i = 0; i < selectTabActions.Length; i++)
        {
            var index = i;
            list.Add(Define(selectTabActions[i], "标签页", $"切换到第 {index + 1} 个标签页", null,
                window => window.SelectTabByIndex(index)));
        }

        return list;
    }

    private static ShortcutActionDefinition Define(ShortcutAction action, string category, string displayName,
        string? defaultGesture, Action<TabWindow> execute, Func<bool>? available = null) =>
        new(action, category, displayName, defaultGesture, execute, available);
}

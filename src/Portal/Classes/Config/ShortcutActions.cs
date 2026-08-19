using Avalonia.Controls;
using Avalonia.Input;
using Portal.Core.Classes.Config;
using Portal.Core.Const;
using Portal.Core.Module.Multiplayer;
using Portal.Core.Services;
using Portal.Localization;
using Portal.Views;
using Portal.Views.Pages;
using TioUi.Shared;

namespace Portal.Classes.Config;

public sealed record ShortcutActionDefinition(
    ShortcutAction Action,
    string Category,
    string DisplayName,
    string? DefaultGesture,
    Action<TabWindow> Execute,
    Func<bool>? Available = null)
{
    public bool IsAvailable => Available?.Invoke() ?? true;
}

public sealed record ShortcutCategory(string Name, IReadOnlyList<ShortcutActionDefinition> Items);

public static class ShortcutActions
{
    public static IReadOnlyList<ShortcutActionDefinition> All { get; } = Build();

    public static IReadOnlyList<string> CategoryOrder { get; } =
    [
        CommonLanguageManager.Instance.shortcuts_categoryTabs.CurrentValue(),
        CommonLanguageManager.Instance.shortcuts_categoryPages.CurrentValue(),
        CommonLanguageManager.Instance.shortcuts_categoryDialogs.CurrentValue(),
        CommonLanguageManager.Instance.shortcuts_categoryApp.CurrentValue()
    ];

    public static IReadOnlyList<ShortcutCategory> Categories { get; } = CategoryOrder
        .Select(name => new ShortcutCategory(name, All
            .Where(definition => definition.Category == name && definition.IsAvailable).ToList()))
        .Where(category => category.Items.Count > 0)
        .ToList();

    public static Dictionary<string, string> CreateDefaultBindings()
    {
        return All
            .Where(definition => !string.IsNullOrWhiteSpace(definition.DefaultGesture))
            .ToDictionary(definition => definition.Action.ToString(), definition => definition.DefaultGesture!);
    }

    public static string GetDisplayName(ShortcutAction action)
    {
        return All.First(definition => definition.Action == action).DisplayName;
    }

    public static string? GetDefaultGesture(ShortcutAction action)
    {
        return All.First(definition => definition.Action == action).DefaultGesture;
    }

    public static string? GetStoredGesture(ShortcutAction action)
    {
        return Data.ConfigEntry.Shortcuts.GetGesture(action);
    }

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

    public static string GestureToString(KeyGesture gesture)
    {
        return gesture.ToString("g", null);
    }

    private static List<ShortcutActionDefinition> Build()
    {
        var list = new List<ShortcutActionDefinition>
        {
            Define(ShortcutAction.NewTab, CommonLanguageManager.Instance.shortcuts_categoryTabs.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_newTab.CurrentValue(), "Ctrl+T",
                window => window.CreateNewTabFunc?.Invoke()),
            Define(ShortcutAction.CloseTab, CommonLanguageManager.Instance.shortcuts_categoryTabs.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_closeTab.CurrentValue(), "Ctrl+W",
                window => window.SelectedTab?.Close()),
            Define(ShortcutAction.CloseOtherTabs, CommonLanguageManager.Instance.shortcuts_categoryTabs.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_closeOtherTabs.CurrentValue(), null,
                window => window.SelectedTab?.CloseOther()),
            Define(ShortcutAction.CloseAllTabs, CommonLanguageManager.Instance.shortcuts_categoryTabs.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_closeAllTabs.CurrentValue(), "Ctrl+Shift+W",
                window => window.CloseAllTab()),
            Define(ShortcutAction.OpenInNewWindow, CommonLanguageManager.Instance.shortcuts_categoryTabs.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_openInNewWindow.CurrentValue(), null,
                window => window.SelectedTab?.MoveTabToNewWindow()),
            Define(ShortcutAction.NextTab, CommonLanguageManager.Instance.shortcuts_categoryTabs.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_nextTab.CurrentValue(), null, window =>
            {
                var tabs = window.Tabs;
                if (tabs.Count < 2 || window.SelectedTab is null) return;
                window.SelectTab(tabs[(tabs.IndexOf(window.SelectedTab) + 1) % tabs.Count]);
            }),
            Define(ShortcutAction.PreviousTab, CommonLanguageManager.Instance.shortcuts_categoryTabs.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_previousTab.CurrentValue(), null, window =>
            {
                var tabs = window.Tabs;
                if (tabs.Count < 2 || window.SelectedTab is null) return;
                window.SelectTab(tabs[(tabs.IndexOf(window.SelectedTab) - 1 + tabs.Count) % tabs.Count]);
            }),
            Define(ShortcutAction.MoveTabForward, CommonLanguageManager.Instance.shortcuts_categoryTabs.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_moveTabForward.CurrentValue(), null,
                window => window.SelectedTab?.MoveTabForward()),
            Define(ShortcutAction.MoveTabBackward, CommonLanguageManager.Instance.shortcuts_categoryTabs.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_moveTabBackward.CurrentValue(), null,
                window => window.SelectedTab?.MoveTabBackward()),

            Define(ShortcutAction.OpenStartPage, CommonLanguageManager.Instance.shortcuts_categoryPages.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_openStartPage.CurrentValue(), null,
                window => window.OpenPage(new StartPage())),
            Define(ShortcutAction.OpenNewTabPage, CommonLanguageManager.Instance.shortcuts_categoryPages.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_openNewTabPage.CurrentValue(), null,
                window => window.OpenPage(new NewTabPage())),
            Define(ShortcutAction.OpenWidgetsPage, CommonLanguageManager.Instance.shortcuts_categoryPages.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_openWidgetsPage.CurrentValue(), null,
                window => window.OpenPage(new WidgetsPage())),
            Define(ShortcutAction.OpenDownloadPage, CommonLanguageManager.Instance.shortcuts_categoryPages.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_openDownloadPage.CurrentValue(), null,
                window => window.OpenPage(new DownloadPage())),
            Define(ShortcutAction.OpenInstancesPage, CommonLanguageManager.Instance.shortcuts_categoryPages.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_openInstancesPage.CurrentValue(), null,
                window => window.OpenPage(new InstancesPage())),
            Define(ShortcutAction.OpenMultiplayerJava, CommonLanguageManager.Instance.shortcuts_categoryPages.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_openMultiplayerJava.CurrentValue(), null,
                window => window.OpenPage(new MultiplayerPage(MinecraftEdition.Java))),
            Define(ShortcutAction.OpenMultiplayerBedrock, CommonLanguageManager.Instance.shortcuts_categoryPages.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_openMultiplayerBedrock.CurrentValue(), null,
                window => window.OpenPage(new MultiplayerPage(MinecraftEdition.Bedrock)),
                () => OperatingSystem.IsWindows()),
            Define(ShortcutAction.OpenNewsPage, CommonLanguageManager.Instance.shortcuts_categoryPages.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_openNewsPage.CurrentValue(), null,
                window => window.OpenPage(new NewsPage())),
            Define(ShortcutAction.OpenToolsPage, CommonLanguageManager.Instance.shortcuts_categoryPages.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_openToolsPage.CurrentValue(), null,
                window => window.OpenPage(new ToolsPage())),
            Define(ShortcutAction.OpenTaskPage, CommonLanguageManager.Instance.shortcuts_categoryPages.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_openTaskPage.CurrentValue(), null,
                window => window.OpenPage(new TaskPage())),
            Define(ShortcutAction.OpenSettingsPage, CommonLanguageManager.Instance.shortcuts_categoryPages.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_openSettingsPage.CurrentValue(), null,
                window => window.OpenPage(new SettingPage())),

            Define(ShortcutAction.OpenAggregatedSearch, CommonLanguageManager.Instance.shortcuts_categoryDialogs.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_openAggregatedSearch.CurrentValue(), "Shift+S",
                window => window.OpenAggregatedSearchDialog()),
            Define(ShortcutAction.OpenCreateInstanceDialog, CommonLanguageManager.Instance.shortcuts_categoryDialogs.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_createInstance.CurrentValue(), null,
                window => window.OpenCreateInstanceDialog()),
            Define(ShortcutAction.AddMinecraftFolder, CommonLanguageManager.Instance.shortcuts_categoryDialogs.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_addMinecraftFolder.CurrentValue(), null,
                window => window.OpenAddMinecraftFolderDialog()),

            Define(ShortcutAction.ToggleTheme, CommonLanguageManager.Instance.shortcuts_categoryApp.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_toggleTheme.CurrentValue(), "Ctrl+Shift+Q", _ =>
            {
                Data.ConfigEntry.Theme = Data.ConfigEntry.Theme switch
                {
                    Theme.Light => Theme.Dark,
                    Theme.Dark => Theme.Mirage,
                    _ => Theme.Light
                };
            }),
#if DEBUG
            Define(ShortcutAction.OpenDebugPage, CommonLanguageManager.Instance.shortcuts_categoryApp.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_openDebugPage.CurrentValue(), "Shift+F12",
                window => window.OpenDebugPage()),
#endif
            Define(ShortcutAction.RestartApp, CommonLanguageManager.Instance.shortcuts_categoryApp.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_restartApp.CurrentValue(), null, _ => AppLifecycle.RestartApp()),
            Define(ShortcutAction.ExitApp, CommonLanguageManager.Instance.shortcuts_categoryApp.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_exitApp.CurrentValue(), null, _ => AppLifecycle.TryExitApp()),
            Define(ShortcutAction.MinimizeWindow, CommonLanguageManager.Instance.shortcuts_categoryApp.CurrentValue(),
                CommonLanguageManager.Instance.shortcuts_minimizeWindow.CurrentValue(), null,
                window => window.WindowState = WindowState.Minimized)
        };


        ShortcutAction[] selectTabActions =
        [
            ShortcutAction.SelectTab1, ShortcutAction.SelectTab2, ShortcutAction.SelectTab3,
            ShortcutAction.SelectTab4, ShortcutAction.SelectTab5, ShortcutAction.SelectTab6,
            ShortcutAction.SelectTab7, ShortcutAction.SelectTab8, ShortcutAction.SelectTab9
        ];
        for (var i = 0; i < selectTabActions.Length; i++)
        {
            var index = i;
            list.Add(Define(selectTabActions[i], CommonLanguageManager.Instance.shortcuts_categoryTabs.CurrentValue(),
                string.Format(CommonLanguageManager.Instance.shortcuts_selectTab.CurrentValue(), index + 1), null,
                window => window.SelectTabByIndex(index)));
        }

        return list;
    }

    private static ShortcutActionDefinition Define(ShortcutAction action, string category, string displayName,
        string? defaultGesture, Action<TabWindow> execute, Func<bool>? available = null)
    {
        return new ShortcutActionDefinition(action, category, displayName, defaultGesture, execute, available);
    }
}
namespace Portal.Classes.Entries;

/// <summary>
/// 可绑定快捷键的全局操作。
/// </summary>
public enum ShortcutAction
{
    // 标签页
    NewTab,
    CloseTab,
    CloseOtherTabs,
    CloseAllTabs,
    OpenInNewWindow,
    NextTab,
    PreviousTab,
    MoveTabForward,
    MoveTabBackward,
    SelectTab1,
    SelectTab2,
    SelectTab3,
    SelectTab4,
    SelectTab5,
    SelectTab6,
    SelectTab7,
    SelectTab8,
    SelectTab9,

    // 页面
    OpenStartPage,
    OpenNewTabPage,
    OpenWidgetsPage,
    OpenDownloadPage,
    OpenInstancesPage,
    OpenMultiplayerJava,
    OpenMultiplayerBedrock,
    OpenNewsPage,
    OpenToolsPage,
    OpenTaskPage,
    OpenSettingsPage,

    // 对话框
    OpenAggregatedSearch,
    OpenCreateInstanceDialog,
    AddMinecraftFolder,

    // 应用
    ToggleTheme,
    OpenDebugPage,
    RestartApp,
    ExitApp,
    MinimizeWindow
}

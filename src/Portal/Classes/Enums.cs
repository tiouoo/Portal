namespace Portal.Classes.Enums;

public enum InstanceSortType
{
    Name,
    PlayTime,
    FolderName,
    Loader,
    Version
}

public enum FilePicker
{
    System,
    Managed,
    Input
}

public enum NoticeWay
{
    Toast,
    Notification
}

public enum PortalVisibleMode
{
    NoOperation,
    QuitAfterLaunch,
    HiddenAfterLaunchAndReopen,
    MinimizedAfterLaunch,
    MinimizedAfterLaunchAndRestore
}

public enum GithubMirrorMode
{
    /// <summary>前缀代理式：https://ghfast.top/https://github.com/a/b</summary>
    Prefix,
    /// <summary>直接访问式（替换域名）：https://bgithub.xyz/a/b</summary>
    Direct
}

public enum NewTabContent
{
    NewTabPage,
    StartPage
}

namespace Portal.Core.Classes;

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
    Prefix,
    Direct
}

public enum UpdateSource
{
    Github,
    Cnb
}

public enum NewTabContent
{
    NewTabPage,
    StartPage,
    Widget
}

public enum DownloadSearchSource
{
    CurseForge,
    Modrinth
}

public enum DownloadSearchSort
{
    Relevance,
    Popularity,
    Updated,
    Newest
}
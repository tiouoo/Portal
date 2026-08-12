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

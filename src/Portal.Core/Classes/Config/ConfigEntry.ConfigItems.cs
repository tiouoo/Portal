using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Base.Enums;
using Portal.Core.Classes;
using Portal.Core.Classes.Entries;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance.Java;
using Tio.Avalonia.Standard.Modules.DiskIO;
using TioUi.Shared;

namespace Portal.Core.Classes.Config;

public partial class ConfigEntry : ObservableObject
{
    [ObservableProperty] public partial bool IsInitialized { get; set; } = true;
    [ObservableProperty] public partial bool EnableCustomForegroundColor { get; set; } = false;
    [ObservableProperty] public partial bool EnableCheckAutoUpdate { get; set; } = true;
    [ObservableProperty] public partial UpdateSource UpdateSource { get; set; } = UpdateSource.Cnb;
    [ObservableProperty] public partial DownloadSourceMode MinecraftMetadataSource { get; set; } = DownloadSourceMode.Auto;
    [ObservableProperty] public partial DownloadSourceMode MinecraftFileSource { get; set; } = DownloadSourceMode.Auto;
    [ObservableProperty] public partial ResourceDownloadSourceMode ResourceDownloadSource { get; set; } = ResourceDownloadSourceMode.Auto;
    [ObservableProperty] public partial ResourceDownloadSourceMode ModrinthResourceDownloadSource { get; set; } = ResourceDownloadSourceMode.Auto;
    [ObservableProperty] public partial ResourceDownloadSourceMode CurseForgeResourceDownloadSource { get; set; } = ResourceDownloadSourceMode.Auto;
    [ObservableProperty] public partial bool EnableFragmentDownload { get; set; }
    [ObservableProperty] public partial bool EnableGameOverlay { get; set; } = true;
    [ObservableProperty] public partial bool EnableFullscreen { get; set; }
    [ObservableProperty] public partial bool AutoSetChineseLanguage { get; set; } = true;
    [ObservableProperty] public partial bool EnableManagedWindowDecorationsOnWindows { get; set; }
    [ObservableProperty] public partial bool EnableManagedWindowBorderOnWindows { get; set; } = true;
    [ObservableProperty] public partial string Language { get; set; } = "zh-CN";
    [ObservableProperty] public partial bool EnableCustomUserAgent { get; set; }
    [ObservableProperty] public partial bool EnableProxyServer { get; set; }
    [ObservableProperty] public partial bool DisableSystemProxy { get; set; }
    [ObservableProperty] public partial bool EnableGithubMirror { get; set; }
    [ObservableProperty] public partial bool EnableDebugConsole { get; set; }
    [ObservableProperty] public partial Logger.LogLevel MinimumLogLevel { get; set; } = Logger.LogLevel.Info;
    [ObservableProperty] public partial bool AutoSetJavaHighPerformanceGpu { get; set; } = true;
    [ObservableProperty] public partial bool AutoOptimizeMemoryBeforeGameLaunch { get; set; }
    [ObservableProperty] public partial bool EnableBedrockAccountInjection { get; set; }
    [ObservableProperty] public partial bool ShowDragDropTip { get; set; } = true;
    [ObservableProperty] public partial bool ShowUpdateTip { get; set; } = true;
    [ObservableProperty] public partial bool ShowUsingAccountTip { get; set; } = true;
    [ObservableProperty] public partial bool ShowMinecraftNews { get; set; } = false;
    [ObservableProperty] public partial bool ShowRecentPlays { get; set; } = true;
    [ObservableProperty] public partial bool NewTabRecentPlaysExpanded { get; set; } = true;
    [ObservableProperty] public partial bool StartPageRecentPlaysExpanded { get; set; }
    [ObservableProperty] public partial bool DownloadJavaEditionExpanded { get; set; } = true;
    [ObservableProperty] public partial bool DownloadBedrockEditionExpanded { get; set; }
    [ObservableProperty] public partial bool DownloadOthersExpanded { get; set; }
    [ObservableProperty] public partial bool SettingsNavGeneralExpanded { get; set; } = true;
    [ObservableProperty] public partial bool SettingsNavGameExpanded { get; set; } = true;
    [ObservableProperty] public partial bool SettingsNavNetworkExpanded { get; set; } = true;
    [ObservableProperty] public partial DownloadSearchSource DefaultDownloadSearchSource { get; set; } = DownloadSearchSource.All;
    [ObservableProperty] public partial ModLoaderType DownloadSearchLoader { get; set; } = ModLoaderType.Any;
    [ObservableProperty] public partial DownloadSearchSort DefaultDownloadSearchSort { get; set; } = DownloadSearchSort.Relevance;
    [ObservableProperty] public partial int ResourceListSortIndex { get; set; }
    [ObservableProperty] public partial string DownloadLastSelectedPage { get; set; } = string.Empty;
    [ObservableProperty] public partial string? BackgroundImagePath { get; set; }
    [ObservableProperty] public partial string? CustomUserAgent { get; set; }
    [ObservableProperty] public partial string? ProxyServer { get; set; }
    [ObservableProperty] public partial string? GithubMirrorUrl { get; set; }
    [ObservableProperty] public partial string? CustomLauncherInfo { get; set; }
    [ObservableProperty] public partial string? OnlinePlayerName { get; set; }
    [ObservableProperty] public partial string DefaultPage { get; set; } = string.Empty;
    [ObservableProperty] public partial string? OverrideMinecraftWindowTitle { get; set; }
    [ObservableProperty] public partial string? BeforeLaunchCommand { get; set; }
    [ObservableProperty] public partial string? AfterLaunchCommand { get; set; }
    [ObservableProperty] public partial string? JvmArgs { get; set; }
    [ObservableProperty] public partial string? PackagedCommand { get; set; }
    [ObservableProperty] public partial Color BackgroundSolidColor { get; set; } = Color.Parse("#2d2d2d");
    [ObservableProperty] public partial Color ForegroundColor { get; set; } = Color.Parse("#494c4f");
    [ObservableProperty] public partial Color ThemeColor { get; set; } = Color.Parse("#1890ff");
    [ObservableProperty] public partial Color CustomWindowBorderColor { get; set; } = Color.Parse("#6a6c70");
    [ObservableProperty] public partial double CustomWindowBorderCornerRadius { get; set; } = 10;
    [ObservableProperty] public partial NewTabContent NewTabContent { get; set; } = NewTabContent.NewTabPage;
    [ObservableProperty] public partial GithubMirrorMode GithubMirrorMode { get; set; } = GithubMirrorMode.Prefix;
    [ObservableProperty] public partial NoticeWay NoticeWay { get; set; } = NoticeWay.Toast;
    [ObservableProperty] public partial Theme Theme { get; set; } = Theme.Light;
    [ObservableProperty] public partial FilePicker FilePicker { get; set; } = FilePicker.System;
    [ObservableProperty] public partial BackgroundMode BackgroundMode { get; set; } = BackgroundMode.Default;
    [ObservableProperty] public partial PortalVisibleMode PortalVisibleMode { get; set; } = PortalVisibleMode.NoOperation;
    [ObservableProperty] public partial InstanceSortType DefaultInstanceSortType { get; set; } = InstanceSortType.PlayTime;
    [ObservableProperty] public partial int DownloadMaxThreadCount { get; set; } = 64;
    [ObservableProperty] public partial int DownloadMaxRetryCount { get; set; } = 4;
    [ObservableProperty] public partial int DownloadMaxFragmentCount { get; set; } = 32;
    [ObservableProperty] public partial int CustomDownloadMaxFragmentCount { get; set; } = 8;
    [ObservableProperty] public partial int MinecraftWindowWidth { get; set; } = 854;
    [ObservableProperty] public partial int MinecraftWindowHeight { get; set; } = 480;
    [ObservableProperty] public partial int MinecraftMaxMemory { get; set; } = 4096;
    [ObservableProperty] public partial double ControlOpacity { get; set; } = 1;
    [ObservableProperty] public partial double TranslucentControlOpacity { get; set; } = 1;
    [ObservableProperty] public partial double AcrylicOpacity { get; set; } = 0.2;
    [ObservableProperty] public partial double ImageBlurRadius { get; set; } = 0.0;
    [ObservableProperty] public partial double MicaOpacity { get; set; } = 0.8;
    [ObservableProperty] public partial double BlurOpacity { get; set; } = 0.5;
    [ObservableProperty] public partial bool EnableImageMask { get; set; }
    [ObservableProperty] public partial Color ImageMaskColor { get; set; } = Color.Parse("#000000");
    [ObservableProperty] public partial double ImageMaskOpacity { get; set; } = 0.3;
    [ObservableProperty] public partial bool ShowWidgetBackground { get; set; } = true;
    [ObservableProperty] public partial double AppScale { get; set; } = 1.0;
    [ObservableProperty] public partial double TabWindowWidth { get; set; }
    [ObservableProperty] public partial double TabWindowHeight { get; set; }
    [ObservableProperty] public partial bool HasTabWindowSize { get; set; }
    [ObservableProperty] public partial List<WidgetLayoutData> WidgetLayout { get; set; } = [];
    [ObservableProperty] public partial List<GameListWidgetEntry> HomePageItems { get; set; } = [];
    [ObservableProperty] public partial MinecraftAccount? UsingMinecraftMinecraftAccount { get; set; }
    [ObservableProperty] public partial BedrockAccount? UsingBedrockAccount { get; set; }
    [ObservableProperty] public partial MinecraftFolderEntry? DefaultMinecraftFolder { get; set; }
    [ObservableProperty] public partial Dictionary<int, string> JavaVersionDefaultPaths { get; set; } = new();
    [ObservableProperty] public partial ShortcutConfig Shortcuts { get; set; } = new();
}

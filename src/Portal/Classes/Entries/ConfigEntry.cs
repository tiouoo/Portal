using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch;
using Portal.Views;
using Portal.Views.Pages;
using Portal.Classes.Enums;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance.Java;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Helpers;
using TioUi.Shared;

namespace Portal.Classes.Entries;

public partial class ConfigEntry : ObservableObject
{
    private bool _isMinecraftFolderRecoveryScheduled;

    public ConfigEntry()
    {
        PropertyChanged += OnPropertyChanged;
        MinecraftAccounts.CollectionChanged += (_, _) => App.Method.SaveConfig();
        AuthServers.CollectionChanged += (_, _) => App.Method.SaveConfig();
        MinecraftFolders.CollectionChanged += OnMinecraftFoldersChanged;
        JavaRuntimes.CollectionChanged += (_, _) => App.Method.SaveConfig();
    }

    [ObservableProperty] public partial Theme Theme { get; set; } = Theme.Light;
    [ObservableProperty] public partial string DefaultPage { get; set; } = typeof(NewTabPage).AssemblyQualifiedName!;
    [ObservableProperty] public partial Color ThemeColor { get; set; } = Color.Parse("#1890ff");
    [ObservableProperty] public partial NoticeWay NoticeWay { get; set; } = NoticeWay.Toast;
    [ObservableProperty] public partial FilePicker FilePicker { get; set; } = FilePicker.System;
    [ObservableProperty] public partial BackgroundMode BackgroundMode { get; set; } = BackgroundMode.Default;
    [ObservableProperty] public partial PortalVisibleMode PortalVisibleMode { get; set; } = PortalVisibleMode.NoOperation;
    [ObservableProperty] public partial InstanceSortType DefaultInstanceSortType { get; set; } = InstanceSortType.PlayTime;
    [ObservableProperty] public partial bool EnableCustomForegroundColor { get; set; } = false;
    [ObservableProperty] public partial bool EnableCheckAutoUpdate { get; set; } = true;
    [ObservableProperty] public partial bool EnableMinecraftMirror { get; set; }
    [ObservableProperty] public partial bool EnableFragmentDownload { get; set; }
    [ObservableProperty] public partial bool EnableCustomUserAgent { get; set; }
    [ObservableProperty] public partial bool EnableAutoSelectJava { get; set; } = true;
    [ObservableProperty] public partial bool ShowDragDropTip { get; set; } = true;
    [ObservableProperty] public partial bool ShowUpdateTip { get; set; } = true;
    [ObservableProperty] public partial bool ShowUsingAccountTip { get; set; } = true;
    [ObservableProperty] public partial bool ShowMinecraftNews { get; set; } = true;
    [ObservableProperty] public partial bool ShowRecentPlays { get; set; } = true;
    [ObservableProperty] public partial string? BackgroundImagePath { get; set; }
    [ObservableProperty] public partial string? CustomUserAgent { get; set; }
    [ObservableProperty] public partial string? CustomLauncherInfo { get; set; }
    [ObservableProperty] public partial string? OverrideMinecraftWindowTitle { get; set; }
    [ObservableProperty] public partial string? BeforeLaunchCommand { get; set; }
    [ObservableProperty] public partial string? JvmArgs { get; set; }
    [ObservableProperty] public partial string? PackagedCommand { get; set; }
    [ObservableProperty] public partial Color BackgroundSolidColor { get; set; } = Color.Parse("#2d2d2d");
    [ObservableProperty] public partial Color ForegroundColor { get; set; } = Color.Parse("#494c4f");
    [ObservableProperty] public partial int DownloadMaxThreadCount { get; set; } = 256;
    [ObservableProperty] public partial int DownloadMaxRetryCount { get; set; } = 4;
    [ObservableProperty] public partial int DownloadMaxFragmentCount { get; set; } = 128;
    [ObservableProperty] public partial int MinecraftWindowWidth { get; set; } = 854;
    [ObservableProperty] public partial int MinecraftWindowHeight { get; set; } = 480;
    [ObservableProperty] public partial int MinecraftMaxMemory { get; set; } = 4096;
    [ObservableProperty] public partial double ControlOpacity { get; set; } = 1;
    [ObservableProperty] public partial double TranslucentControlOpacity { get; set; } = 1;
    [ObservableProperty] public partial double AcrylicOpacity { get; set; } = 0.2;
    [ObservableProperty] public partial double ImageBlurRadius { get; set; } = 0.0;
    [ObservableProperty] public partial double MicaOpacity { get; set; } = 0.8;
    [ObservableProperty] public partial double BlurOpacity { get; set; } = 0.5;
    [ObservableProperty] public partial MinecraftAccount? UsingMinecraftMinecraftAccount { get; set; }
    [ObservableProperty] public partial MinecraftFolderEntry? DefaultMinecraftFolder { get; set; }
    [ObservableProperty] public partial JavaRuntimeEntry? DefaultJavaRuntime { get; set; }
    public ObservableCollection<MinecraftAccount> MinecraftAccounts { get; } = [];
    public ObservableCollection<MinecraftFolderEntry> MinecraftFolders { get; } = [];
    public ObservableCollection<AuthServer> AuthServers { get; } = [];
    public ObservableCollection<JavaRuntimeEntry> JavaRuntimes { get; } = [];

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Theme):
                ThemeHelper.ToggleTheme(Theme);
                break;
            case nameof(ThemeColor):
                ThemeHelper.SetThemeColor(ThemeColor);
                break;
            case nameof(ForegroundColor):
            case nameof(EnableCustomForegroundColor):
                ApplyForegroundColor();
                break;
            case nameof(BackgroundImagePath):
            case nameof(BackgroundSolidColor):
            case nameof(AcrylicOpacity):
            case nameof(ImageBlurRadius):
            case nameof(MicaOpacity):
            case nameof(BlurOpacity):
                TabWindow.ApplyBackgroundToAllWindows();
                break;
            case nameof(ControlOpacity):
            case nameof(TranslucentControlOpacity):
                SetResource();
                break;
            case nameof(BackgroundMode):
                TabWindow.ApplyBackgroundToAllWindows();
                SetResource();
                break;
            case nameof(EnableFragmentDownload):
                DownloadManager.IsEnableFragment = EnableFragmentDownload;
                break;
            case nameof(EnableMinecraftMirror):
                DownloadManager.IsEnableMirror = EnableMinecraftMirror;
                break;
            case nameof(DownloadMaxThreadCount):
                DownloadManager.MaxThread = DownloadMaxThreadCount;
                break;
            case nameof(DownloadMaxRetryCount):
                DownloadManager.MaxRetryCount = DownloadMaxRetryCount;
                break;
            case nameof(DownloadMaxFragmentCount):
                DownloadManager.MaxFragment = DownloadMaxFragmentCount;
                break;
        }

        if (Data.UiProperty.ConfigLoaded &&
            (e.PropertyName != nameof(DefaultMinecraftFolder) || MinecraftFolders.Count > 0))
        {
            ConfigIdentifyExtension.MinecraftFolder(this);
        }

        App.Method.SaveConfig();
    }

    private void OnMinecraftFoldersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        App.Method.SaveConfig();
        if (!Data.UiProperty.ConfigLoaded || _isMinecraftFolderRecoveryScheduled)
            return;

        _isMinecraftFolderRecoveryScheduled = true;
        // The default-folder ComboBox may update its selection during this notification.
        // Recover after it completes so ObservableCollection is no longer in its reentrancy guard.
        Dispatcher.UIThread.Post(() =>
        {
            _isMinecraftFolderRecoveryScheduled = false;
            ConfigIdentifyExtension.MinecraftFolder(this);
        });
    }

    private void SetResource()
    {
        if (BackgroundMode == BackgroundMode.Default)
        {
            Application.Current.Resources.Remove("BackGroundOpacity");
            Application.Current.Resources.Remove("TranslucentBackGroundOpacity");
        }
        else
        {
            Application.Current.Resources["BackGroundOpacity"] = ControlOpacity;
            Application.Current.Resources["TranslucentBackGroundOpacity"] = TranslucentControlOpacity;
        }
    }

    private void ApplyForegroundColor()
    {
        if (EnableCustomForegroundColor)
        {
            SetForegroundColor(ForegroundColor);
        }
        else
        {
            ClearForegroundColor();
        }
    }

    public static void SetForegroundColor(Color color)
    {
        var app = Application.Current;
        if (app?.Resources == null) return;

        app.Resources["ForegroundColor"] = new SolidColorBrush(color);
        app.Resources["InnerForegroundColor"] = new SolidColorBrush(
            Color.FromRgb((byte)(color.R * 0.9), (byte)(color.G * 0.9), (byte)(color.B * 0.9)));
    }

    public static void ClearForegroundColor()
    {
        var app = Application.Current;
        if (app?.Resources == null) return;

        app.Resources.Remove("ForegroundColor");
        app.Resources.Remove("InnerForegroundColor");
    }
}

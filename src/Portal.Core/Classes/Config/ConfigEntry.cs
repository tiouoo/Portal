using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch;
using Portal.Core.App.Events;
using Portal.Core.Classes.Entries;
using Portal.Core.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Instance.Java;
using Portal.Core.Module.Initialize;
using Tio.Avalonia.Standard.Modules.DiskIO;
using TioUi.Common.Helpers;

namespace Portal.Core.Classes.Config;

public partial class ConfigEntry : ObservableObject
{
    private bool _isMinecraftFolderRecoveryScheduled;
    private bool _isMinecraftFolderRefreshScheduled;
    private readonly HashSet<MinecraftFolderEntry> _observedMinecraftFolders = [];

    public ConfigEntry()
    {
        PropertyChanged += OnPropertyChanged;
        MinecraftAccounts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasJavaAccounts));
            OnPropertyChanged(nameof(HasBothAccountEditions));
            OnPropertyChanged(nameof(HasAnyAccounts));
            OnPropertyChanged(nameof(CurrentAccountDisplay));
            ConfigSaver.SaveConfig();
        };
        BedrockAccounts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasBedrockAccounts));
            OnPropertyChanged(nameof(HasBothAccountEditions));
            OnPropertyChanged(nameof(HasAnyAccounts));
            OnPropertyChanged(nameof(CurrentAccountDisplay));
            ConfigSaver.SaveConfig();
        };
        AuthServers.CollectionChanged += (_, _) => ConfigSaver.SaveConfig();
        MinecraftFolders.CollectionChanged += OnMinecraftFoldersChanged;
        JavaRuntimes.CollectionChanged += (_, _) => ConfigSaver.SaveConfig();
    }

    public ObservableCollection<MinecraftAccount> MinecraftAccounts { get; } = [];
    public ObservableCollection<BedrockAccount> BedrockAccounts { get; } = [];
    public bool HasJavaAccounts => MinecraftAccounts.Count > 0;
    public bool HasBedrockAccounts => !OperatingSystem.IsMacOS() && BedrockAccounts.Count > 0;
    public bool HasBothAccountEditions => HasJavaAccounts && HasBedrockAccounts;
    public bool HasAnyAccounts => HasJavaAccounts || HasBedrockAccounts;
    public string CurrentAccountDisplay => UsingMinecraftMinecraftAccount?.ShortDisplay
                                           ?? (OperatingSystem.IsMacOS() ? null : UsingBedrockAccount?.ShortDisplay)
                                           ?? "无账户";
    public ObservableCollection<MinecraftFolderEntry> MinecraftFolders { get; } = [];
    public bool CanDisableSystemProxy => !EnableProxyServer;

    public IEnumerable<MinecraftFolderEntry> InstallableMinecraftFolders =>
        MinecraftFolders.Where(folder => folder.DetectedLayout.Kind == MinecraftFolderKind.PortalMc);

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
            case nameof(CustomWindowBorderColor):
            case nameof(EnableManagedWindowBorderOnWindows):
            case nameof(EnableManagedWindowDecorationsOnWindows):
                UiEvents.RaiseBackgroundAppearanceChanged();
                break;
            case nameof(EnableImageMask):
            case nameof(ImageMaskColor):
            case nameof(ImageMaskOpacity):
                UiEvents.RaiseImageMaskChanged();
                break;
            case nameof(ControlOpacity):
            case nameof(TranslucentControlOpacity):
                SetResource();
                break;
            case nameof(AppScale):
                UiEvents.RaiseAppScaleChanged(AppScale);
                break;
            case nameof(BackgroundMode):
                UiEvents.RaiseBackgroundAppearanceChanged();
                SetResource();
                break;
            case nameof(EnableFragmentDownload):
                DownloadManager.IsEnableFragment = EnableFragmentDownload;
                break;
            case nameof(MinimumLogLevel):
                Logger.MinimumLevel = MinimumLogLevel;
                break;
            case nameof(MinecraftMetadataSource):
                DownloadManager.MinecraftMetadataSource = MinecraftMetadataSource;
                break;
            case nameof(MinecraftFileSource):
                DownloadManager.MinecraftFileSource = MinecraftFileSource;
                break;
            case nameof(ModrinthSource):
                DownloadManager.ModrinthSource = ModrinthSource;
                break;
            case nameof(CurseForgeSource):
                DownloadManager.CurseForgeSource = CurseForgeSource;
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
            case nameof(EnableProxyServer):
                if (EnableProxyServer && !DisableSystemProxy)
                    DisableSystemProxy = true;
                OnPropertyChanged(nameof(CanDisableSystemProxy));
                break;
            case nameof(DisableSystemProxy):
                if (EnableProxyServer && !DisableSystemProxy)
                    DisableSystemProxy = true;
                break;
        }

        if (Data.UiProperty.ConfigLoaded && e.PropertyName == nameof(DefaultMinecraftFolder) &&
            MinecraftFolders.Count > 0)
        {
            ScheduleMinecraftFolderRecovery();
        }

        ConfigSaver.SaveConfig();
    }

    partial void OnUpdateSourceChanged(UpdateSource value)
    {
        if (value != UpdateSource.Github) Data.UiProperty.OverrideUpdateChannel = "release";
    }

    partial void OnUsingMinecraftMinecraftAccountChanged(MinecraftAccount? value) =>
        OnPropertyChanged(nameof(CurrentAccountDisplay));

    partial void OnUsingBedrockAccountChanged(BedrockAccount? value) =>
        OnPropertyChanged(nameof(CurrentAccountDisplay));

    private void OnMinecraftFoldersChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (MinecraftFolderEntry folder in e.OldItems)
            {
                folder.PropertyChanged -= OnMinecraftFolderPropertyChanged;
                _observedMinecraftFolders.Remove(folder);
            }
        }

        if (e.NewItems != null)
        {
            foreach (MinecraftFolderEntry folder in e.NewItems)
            {
                if (_observedMinecraftFolders.Add(folder))
                    folder.PropertyChanged += OnMinecraftFolderPropertyChanged;
            }
        }

        OnPropertyChanged(nameof(InstallableMinecraftFolders));
        ConfigSaver.SaveConfig();
        ScheduleMinecraftFolderRecovery();
        ScheduleMinecraftFolderRefresh();
    }

    private void OnMinecraftFolderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Data.UiProperty.ConfigLoaded || e.PropertyName != nameof(MinecraftFolderEntry.FolderPath))
            return;
        OnPropertyChanged(nameof(InstallableMinecraftFolders));
        ConfigIdentifyExtension.MinecraftFolder(this);
    }

    private void ScheduleMinecraftFolderRecovery()
    {
        if (!Data.UiProperty.ConfigLoaded || _isMinecraftFolderRecoveryScheduled)
            return;

        _isMinecraftFolderRecoveryScheduled = true;
        
        
        Dispatcher.UIThread.Post(() =>
        {
            _isMinecraftFolderRecoveryScheduled = false;
            ConfigIdentifyExtension.MinecraftFolder(this);
        });
    }

    private void ScheduleMinecraftFolderRefresh()
    {
        if (!Data.UiProperty.ConfigLoaded || _isMinecraftFolderRefreshScheduled)
            return;

        _isMinecraftFolderRefreshScheduled = true;
        _ = RefreshMinecraftFoldersAsync();
    }

    private async Task RefreshMinecraftFoldersAsync()
    {
        try
        {
            var folders = MinecraftFolders.ToArray();
            var instances = await Task.Run(() => InstanceManager.Instance.ScanAll(folders));
            InstanceManager.Instance.ApplyInstances(instances);
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
        }
        finally
        {
            _isMinecraftFolderRefreshScheduled = false;
        }
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

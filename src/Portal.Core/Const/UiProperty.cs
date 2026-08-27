using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Base.Models.Network;
using Portal.Core.Classes.Entries;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Module.AggregatedSearch;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Standard.Ui;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common.Classes;
using Index = Portal.Core.Module.AggregatedSearch.Index;

namespace Portal.Core.Const;

public partial class UiProperty : ObservableObject
{
    private static UiProperty? _instance;

    public UiProperty()
    {
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(AggregatedSearchQuery) or nameof(AggregatedSelectedType))
            {
                AggregatedSearchResults.Clear();
                AggregatedSearchResults.AddRange(Searcher.Search(AggregatedSearchQuery,
                    AggregatedSelectedType.EnumFlag));
            }
        };

        InstanceManager.Instance.InstancesChanged += OnInstancesChanged;
        RecentPlayListService.Instance.Refreshed += OnRecentPlaysRefreshed;
    }

    public static UiProperty Instance
    {
        get { return _instance ??= new UiProperty(); }
    }

    public static ObservableCollection<NotificationEntry> Notifications { get; } = [];
    public static ObservableCollection<NotificationEntry> HistoryNotifications { get; } = [];
    public List<VersionManifestEntry> MinecraftVersionManifestEntries { get; } = [];

    public static IReadOnlyList<Window> Windows => (Application.Current!.ApplicationLifetime as
        IClassicDesktopStyleApplicationLifetime).Windows;

    public static ITioWindow? ActiveWindow => Windows.FirstOrDefault
        (x => x.IsActive) as ITioWindow;

    public static TioTabWindowBase? TabWindow => Windows.FirstOrDefault
        (x => x is TioTabWindowBase) as TioTabWindowBase;

    [ObservableProperty] public partial string AggregatedSearchQuery { get; set; }
    [ObservableProperty] public partial bool ConfigLoaded { get; set; }
    [ObservableProperty] public partial bool FoundNewVersion { get; set; }
    [ObservableProperty] public partial bool IsLatestVersion { get; set; }
    [ObservableProperty] public partial bool IsUpdateDownloading { get; set; }
    [ObservableProperty] public partial bool IsAutomaticUpdateDownloading { get; set; }
    [ObservableProperty] public partial bool IsUpdateReady { get; set; }
    [ObservableProperty] public partial bool IsManualUpdateRequested { get; set; }
    [ObservableProperty] public partial int UpdateDownloadPercent { get; set; }
    [ObservableProperty] public partial int AutomaticUpdateDownloadPercent { get; set; }
    [ObservableProperty] public partial string NewVersion { get; set; }
    [ObservableProperty] public partial string OverrideUpdateChannel { get; set; }
    [ObservableProperty] public partial string? LastModInstallInstancePath { get; set; }
    public ObservableCollection<AggregatedSearchEntry> AggregatedSearchResults { get; set; } = [];

    public bool ShowUpdateReady => IsUpdateReady && (Data.ConfigEntry.EnableCheckAutoUpdate || IsManualUpdateRequested);
    public bool ShowAutomaticUpdateProgress => Data.ConfigEntry.EnableCheckAutoUpdate &&
                                               IsAutomaticUpdateDownloading && !ShowUpdateReady;
    public string AutomaticUpdateProgressText => string.Format(
        ComponentsLanguageManager.Instance.titlebarcomponent_updateProgress.CurrentValue(),
        AutomaticUpdateDownloadPercent);
    public bool ShowNewVersionTip => FoundNewVersion && !IsUpdateDownloading && !ShowUpdateReady;
    public bool ShowUpdateDetails => FoundNewVersion && !IsUpdateDownloading;
    public bool ShowManualUpdateDownload => FoundNewVersion && !IsUpdateDownloading && !IsUpdateReady;

    public void RefreshAutomaticUpdateVisibility()
    {
        OnPropertyChanged(nameof(ShowUpdateReady));
        OnPropertyChanged(nameof(ShowAutomaticUpdateProgress));
        OnPropertyChanged(nameof(ShowNewVersionTip));
        OnPropertyChanged(nameof(ShowUpdateDetails));
        OnPropertyChanged(nameof(ShowManualUpdateDownload));
    }

    partial void OnIsUpdateDownloadingChanged(bool value) => RefreshAutomaticUpdateVisibility();
    partial void OnIsAutomaticUpdateDownloadingChanged(bool value) => RefreshAutomaticUpdateVisibility();
    partial void OnIsUpdateReadyChanged(bool value) => RefreshAutomaticUpdateVisibility();
    partial void OnIsManualUpdateRequestedChanged(bool value) => RefreshAutomaticUpdateVisibility();
    partial void OnFoundNewVersionChanged(bool value) => RefreshAutomaticUpdateVisibility();
    partial void OnAutomaticUpdateDownloadPercentChanged(int value) =>
        OnPropertyChanged(nameof(AutomaticUpdateProgressText));

    [ObservableProperty]
    public partial AggregatedSearchType AggregatedSelectedType { get; set; } = AggregatedSearchTypes[0];

    public static List<AggregatedSearchType> AggregatedSearchTypes { get; set; } =
    [
        new() { DisplayText = CommonLanguageManager.Instance.aggregatedSearch_all.CurrentValue(), EnumFlag = AggregatedSearchEntryType.All },
        new() { DisplayText = CommonLanguageManager.Instance.aggregatedSearch_recentPlay.CurrentValue(), EnumFlag = AggregatedSearchEntryType.RecentPlay },
        new() { DisplayText = CommonLanguageManager.Instance.aggregatedSearch_instance.CurrentValue(), EnumFlag = AggregatedSearchEntryType.Instance },
        new() { DisplayText = CommonLanguageManager.Instance.aggregatedSearch_account.CurrentValue(), EnumFlag = AggregatedSearchEntryType.Account },
        new() { DisplayText = CommonLanguageManager.Instance.aggregatedSearch_page.CurrentValue(), EnumFlag = AggregatedSearchEntryType.Page }
    ];

    private void OnInstancesChanged(object? sender, EventArgs e)
    {
        Index.MarkDirty();
        if (AggregatedSelectedType.EnumFlag.HasFlag(AggregatedSearchEntryType.Instance) ||
            AggregatedSelectedType.EnumFlag == AggregatedSearchEntryType.All)
        {
            AggregatedSearchResults.Clear();
            AggregatedSearchResults.AddRange(Searcher.Search(AggregatedSearchQuery,
                AggregatedSelectedType.EnumFlag));
        }
    }

    private void OnRecentPlaysRefreshed(object? sender, EventArgs e)
    {
        Index.MarkDirty();
        if (AggregatedSelectedType.EnumFlag.HasFlag(AggregatedSearchEntryType.RecentPlay) ||
            AggregatedSelectedType.EnumFlag == AggregatedSearchEntryType.All)
        {
            AggregatedSearchResults.Clear();
            AggregatedSearchResults.AddRange(Searcher.Search(AggregatedSearchQuery,
                AggregatedSelectedType.EnumFlag));
        }
    }
}

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
    [ObservableProperty] public partial string NewVersion { get; set; }
    [ObservableProperty] public partial string OverrideUpdateChannel { get; set; }
    [ObservableProperty] public partial string? LastModInstallInstancePath { get; set; }
    public ObservableCollection<AggregatedSearchEntry> AggregatedSearchResults { get; set; } = [];

    [ObservableProperty]
    public partial AggregatedSearchType AggregatedSelectedType { get; set; } = AggregatedSearchTypes[0];

    public static List<AggregatedSearchType> AggregatedSearchTypes { get; set; } =
    [
        new() { DisplayText = "所有", EnumFlag = AggregatedSearchEntryType.All },
        new() { DisplayText = "最近游玩", EnumFlag = AggregatedSearchEntryType.RecentPlay },
        new() { DisplayText = "实例", EnumFlag = AggregatedSearchEntryType.Instance },
        new() { DisplayText = "账户", EnumFlag = AggregatedSearchEntryType.Account },
        new() { DisplayText = "页面", EnumFlag = AggregatedSearchEntryType.Page }
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
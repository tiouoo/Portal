using System.Diagnostics;
using Avalonia.Media;
using Portal.Core.Const;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Module.AggregatedSearch;
using Portal.Module.DefaultPage;
using Portal.Localization;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Entries;

namespace Portal.Views.Pages;

[AggregatedSearchPage("pages_instances", "pages_instancesPath", "Instances")]
[DefaultPage("pages_instances")]
public partial class InstancesPage : InstanceListPageBase
{
    public InstancesPageViewModel InstancesPageViewModel;
    private bool _isInitialized;

    public InstancesPage()
    {
        InitializeComponent();
        InstancesPageViewModel = new InstancesPageViewModel();
        DataContext = InstancesPageViewModel;
        Loaded += (_, _) =>
        {
            if (_isInitialized)
                return;

            _isInitialized = true;
            Logger.Info("[Instances] Page loaded; applying initial instance filter and sort.");
            InstancesPageViewModel.ApplyFilterAndSort();
        };
    }

    public override PageInfo PageInfo { get; init; } = new()
    {
        Title = CommonLanguageManager.Instance.instancesPage_pageTitle.CurrentValue(),
        Icon = StreamGeometry.Parse(
            "F1 M640,640z M0,0z M480,576L192,576C139,576,96,533,96,480L96,160C96,107,139,64,192,64L496,64C522.5,64,544,85.5,544,112L544,400C544,420.9,530.6,438.7,512,445.3L512,512C529.7,512 544,526.3 544,544 544,561.7 529.7,576 512,576L480,576z M192,448C174.3,448 160,462.3 160,480 160,497.7 174.3,512 192,512L448,512 448,448 192,448z M224,216C224,229.3,234.7,240,248,240L424,240C437.3,240 448,229.3 448,216 448,202.7 437.3,192 424,192L248,192C234.7,192,224,202.7,224,216z M248,288C234.7,288 224,298.7 224,312 224,325.3 234.7,336 248,336L424,336C437.3,336 448,325.3 448,312 448,298.7 437.3,288 424,288L248,288z")
    };

    protected override InstanceListViewModelBase PageViewModel => InstancesPageViewModel;

    public void Refresh()
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.Info(
            $"[Instances] Refreshing instances in {Data.ConfigEntry.MinecraftFolders.Count} configured folder(s).");
        InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
        InstancesPageViewModel.ApplyFilterAndSort();
        Logger.Info(
            $"[Instances] Refreshed {InstanceManager.Instance.Instances.Count} instance(s) in {stopwatch.Elapsed}.");
    }

    protected override Task RefreshInstancesAndRecentPlaysAsync()
    {
        Refresh();
        return Task.CompletedTask;
    }
}

public class InstancesPageViewModel : InstanceListViewModelBase
{
    public InstancesPageViewModel()
    {
        SelectedSortOption = SortOptions.FirstOrDefault(o => o.SortType == Data.ConfigEntry.DefaultInstanceSortType);
        RefreshFolderFilterOptions();
    }

    protected override bool FolderFilterEnabled => true;
}

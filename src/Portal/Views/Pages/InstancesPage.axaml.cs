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

using Portal.Module;
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
        Icon = GeometryResources.Get("DocumentLinesGeometry")
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

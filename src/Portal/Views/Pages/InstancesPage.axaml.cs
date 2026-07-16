using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Module.AggregatedSearch;
using Portal.Module.DefaultPage;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Views.Pages;

[AggregatedSearchPage("实例", "实例", "Instances")]
[DefaultPage("实例")]
public partial class InstancesPage : DataUserControl, ITioTabPage
{
    public InstancesPageViewModel InstancesPageViewModel;

    public InstancesPage()
    {
        InitializeComponent();
        InstancesPageViewModel = new InstancesPageViewModel();
        DataContext = InstancesPageViewModel;
        Loaded += (_, _) => InstancesPageViewModel.ApplyFilterAndSort();
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = "实例",
        Icon = StreamGeometry.Parse(
            "F1 M640,640z M0,0z M480,576L192,576C139,576,96,533,96,480L96,160C96,107,139,64,192,64L496,64C522.5,64,544,85.5,544,112L544,400C544,420.9,530.6,438.7,512,445.3L512,512C529.7,512 544,526.3 544,544 544,561.7 529.7,576 512,576L480,576z M192,448C174.3,448 160,462.3 160,480 160,497.7 174.3,512 192,512L448,512 448,448 192,448z M224,216C224,229.3,234.7,240,248,240L424,240C437.3,240 448,229.3 448,216 448,202.7 437.3,192 424,192L248,192C234.7,192,224,202.7,224,216z M248,288C234.7,288 224,298.7 224,312 224,325.3 234.7,336 248,336L424,336C437.3,336 448,325.3 448,312 448,298.7 437.3,288 424,288L248,288z")
    };

    public TabEntry HostTab { get; set; }

    private void FavoritedButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var instance = (sender as Control)?.Tag as MinecraftInstance;
        if (instance == null || instance.Config == null) return;

        instance.Config.IsFavorite = !instance.Config.IsFavorite;
        instance.SaveConfig();
        InstancesPageViewModel.ApplyFilterAndSort();
    }

    private void RefreshInstance_Click(object? sender, RoutedEventArgs e)
    {
        InstanceManager.Instance.RefreshAll(
            Data.ConfigEntry.MinecraftFolders.Select(f => (f.FolderPath, f.FolderName))
        );
        InstancesPageViewModel.ApplyFilterAndSort();
    }
}

public partial class InstancesPageViewModel : InstanceListViewModelBase
{
    protected override bool FolderFilterEnabled => true;

    public InstancesPageViewModel()
    {
        SelectedSortOption = SortOptions.FirstOrDefault(o => o.SortType == Data.ConfigEntry.DefaultInstanceSortType);
        RefreshFolderFilterOptions();
    }
}

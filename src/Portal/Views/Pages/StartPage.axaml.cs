using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Classes.Entries;
using Portal.Const;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Operations.OpenFile;
using Portal.Module.AggregatedSearch;
using Portal.Module.DefaultPage;
using Portal.Services;
using Portal.ViewModels;
using Portal.Views.Pages.DownloadPages;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;
using Handler = Portal.Module.AggregatedSearch.Handler;

namespace Portal.Views.Pages;

[AggregatedSearchPage("起始页", "起始页", "NewTab")]
[DefaultPage("起始页")]
public partial class StartPage : DataUserControl, ITioTabPage
{
    private readonly StartPageViewModel _viewModel;
    private bool _isInitialized;
    private bool _suppressSearchPopulate;

    public StartPage()
    {
        InitializeComponent();
        _viewModel = new StartPageViewModel();
        DataContext = _viewModel;
        SearchBox.AddHandler(InputElement.KeyDownEvent, SearchBox_OnKeyDown, RoutingStrategies.Bubble,
            handledEventsToo: true);
        Loaded += (_, _) =>
        {
            if (_isInitialized)
                return;

            _isInitialized = true;
            _viewModel.ApplyFilterAndSort();
        };
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = "起始页",
        Icon = StreamGeometry.Parse(
            "F1 M640,640z M0,0z M96.5,160L96.5,309.5C96.5,326.5,103.2,342.8,115.2,354.8L307.2,546.8C332.2,571.8,372.7,571.8,397.7,546.8L547.2,397.3C572.2,372.3,572.2,331.8,547.2,306.8L355.2,114.8C343.2,102.7,327,96,310,96L160.5,96C125.2,96,96.5,124.7,96.5,160z M208.5,176C226.2,176 240.5,190.3 240.5,208 240.5,225.7 226.2,240 208.5,240 190.8,240 176.5,225.7 176.5,208 176.5,190.3 190.8,176 208.5,176z")
    };
    public TabEntry HostTab { get; set; }

    public void OnClose()
    {
        _viewModel.Dispose();
        DataContext = null;
    }

    private void StartPage_OnSizeChanged(object? sender, SizeChangedEventArgs e) =>
        _viewModel.SetRecentPlayWidth(e.NewSize.Width);

    private void SearchBox_OnPopulating(object? sender, PopulatingEventArgs e)
    {
        if (_suppressSearchPopulate)
        {
            _suppressSearchPopulate = false;
            e.Cancel = true;
            return;
        }

        if (_viewModel.SelectedSearchMode?.PageType is not null)
        {
            e.Cancel = true;
            return;
        }

        if (sender is TioUi.Controls.AutoCompleteBox box)
            box.ItemsSource = Searcher.Search(e.Parameter ?? string.Empty);
    }

    private void SearchBox_OnDropDownOpened(object? sender, EventArgs e)
    {
        if (sender is not TioUi.Controls.AutoCompleteBox box)
            return;

        if (_viewModel.SelectedSearchMode?.PageType is not null)
        {
            box.IsDropDownOpen = false;
            return;
        }

        box.ItemsSource = Searcher.Search(box.Text ?? string.Empty);
    }

    private void SearchBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        if (e.Source is Visual visual && visual.FindAncestorOfType<ComboBox>() != null)
            return;

        var mode = _viewModel.SelectedSearchMode;
        if (mode?.PageType is null)
            return;

        var keyword = SearchBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
            return;

        OpenDownloadSearchTab(mode.PageType, keyword, $"{mode.DisplayText}搜索");
        _suppressSearchPopulate = true;
        SearchBox.SelectedItem = null;
        SearchBox.Text = null;
        SearchBox.IsDropDownOpen = false;
        e.Handled = true;
    }

    private void OpenDownloadSearchTab(Type pageType, string keyword, string title)
    {
        if (this.GetTopLevel() is not TioTabWindowBase window)
            return;

        var page = new DownloadSearchTabPage(pageType, keyword, title);
        var tab = new TabEntry(window, page);
        window.CreateTab(tab);
        window.SelectTab(tab);
    }

    private void SearchBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not AggregatedSearchEntry entry)
            return;
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        if (sender is TioUi.Controls.AutoCompleteBox box)
        {
            _suppressSearchPopulate = true;
            box.SelectedItem = null;
            box.Text = null;
            box.IsDropDownOpen = false;
        }

        Handler.HandleAsync(entry, topLevel);
    }

    private void InstanceCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is Visual visual && (visual is Button || visual.FindAncestorOfType<Button>() != null))
            return;

        if (sender is Control { DataContext: MinecraftInstance instance } &&
            TopLevel.GetTopLevel(this) is { } topLevel)
            InstanceDetailPage.Open(instance, topLevel);
    }

    private void LaunchInstance_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not MinecraftInstance instance)
            return;

        _ = MinecraftLaunchService.LaunchAsync(instance, TopLevel.GetTopLevel(this),
            MinecraftLaunchOptionsFactory.Create(logSession => MinecraftLogPage.Open(logSession, this.GetTopLevel())));
    }

    private void FavoritedButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var instance = (sender as Control)?.Tag as MinecraftInstance;
        if (instance == null || instance.Config == null) return;

        instance.Config.IsFavorite = !instance.Config.IsFavorite;
        instance.SaveConfig();
        _viewModel.ApplyFilterAndSort();
    }

    private void ButtonOpenInstance_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is null || sender.AsTopLevel() is not TioTabWindowBase window)
            return;

        var tab = new TabEntry(window, new InstancesPage());
        window.CreateTab(tab);
        window.SelectTab(tab);
    }

    private async void RefreshInstance_Click(object? sender, RoutedEventArgs e)
    {
        await RefreshInstancesAndRecentPlaysAsync();
    }

    private async void RefreshRecentPlays_Click(object? sender, RoutedEventArgs e)
    {
        await RefreshInstancesAndRecentPlaysAsync();
    }

    private async Task RefreshInstancesAndRecentPlaysAsync()
    {
        InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
        _viewModel.ApplyFilterAndSort();
        await RecentPlayListService.Instance.RefreshAsync();
    }

    private async void ImportModpack_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = this.GetTopLevel();
        var file = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入整合包",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("整合包") { Patterns = ["*.mrpack", "*.zip"] }]
        });
        var archivePath = file.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(archivePath))
            return;

        _ = ModpackDetailsPage.TryInstallFromPath(topLevel, archivePath);
    }

    private async void AddFolder_Click(object? sender, RoutedEventArgs e)
    {
        var options = new OverlayDialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            CanResize = false,
            VerticalOffset = 110,
            VerticalAnchor = VerticalPosition.Top
        };

        var result = await OverlayDialog
            .ShowCustomAsync<NewMinecraftFolder, NewMinecraftFolderViewModel, MinecraftFolderEntry>(
                new NewMinecraftFolderViewModel(Data.ConfigEntry.MinecraftFolders.Select(x
                    => x.FolderPath).ToList()), hostId: this.TryGetHostId(), options: options);

        if (result == null) return;
        Data.ConfigEntry.MinecraftFolders.Add(result);
    }

    private async void ToDownload_Click(object? sender, RoutedEventArgs e)
    {
        var options = new OverlayDialogOptions
        {
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            CanResize = false,
            IsCloseButtonVisible = false
        };
        await OverlayDialog.ShowCustomAsync<CreateInstanceDialog, CreateInstanceDialogViewModel, bool>(
            new CreateInstanceDialogViewModel(), this.TryGetHostId(), options);
    }

    private void QuickPlay_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not RecentPlayTarget target || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        _ = MinecraftLaunchService.LaunchAsync(target.Instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(logSession => MinecraftLogPage.Open(logSession, topLevel)), target);
    }

    private void RecentPlayFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is RecentPlayItem item)
            _viewModel.ToggleRecentPlayFavorite(item);
    }
}

public sealed record SearchMode(string DisplayText, Type? PageType);

public partial class StartPageViewModel : InstanceListViewModelBase
{
    private readonly RecentPlayListService _recentPlayListService = RecentPlayListService.Instance;
    private List<RecentPlayItem> _allRecentPlays = [];
    private int _recentPlayCapacity = 1;
    private bool _areRecentPlaysExpanded;
    private bool _isDisposed;

    public IReadOnlyList<SearchMode> SearchModes { get; } =
    [
        new("本地", null),
        new("资源包", typeof(ResourcePackSearchPage)),
        new("存档数据包", typeof(DataPackSearchPage)),
        new("光影材质包", typeof(ShaderPackSearchPage)),
        new("整合包", typeof(ModpackSearchPage)),
        new("模组", typeof(ModSearchPage)),
    ];

    [ObservableProperty] public partial SearchMode? SelectedSearchMode { get; set; }

    public string SearchPlaceholder =>
        SelectedSearchMode?.PageType is null ? "搜索实例、存档、服务器、页面" : $"搜索{SelectedSearchMode.DisplayText}";

    partial void OnSelectedSearchModeChanged(SearchMode? value) => OnPropertyChanged(nameof(SearchPlaceholder));

    public ObservableCollection<RecentPlayItem> RecentPlays { get; } = [];
    public bool HasRecentPlays => RecentPlays.Count > 0;
    public bool CanExpandRecentPlays => _allRecentPlays.Count > _recentPlayCapacity;
    public string ToggleRecentPlaysText => _areRecentPlaysExpanded ? "收起" : $"展开全部 ({_allRecentPlays.Count})";

    public StartPageViewModel()
    {
        SelectedSortOption = SortOptions.FirstOrDefault(o => o.SortType == Data.ConfigEntry.DefaultInstanceSortType);
        SelectedSearchMode = SearchModes[0];
        _recentPlayListService.Refreshed += OnRecentPlaysRefreshed;
        UpdateRecentPlays(_recentPlayListService.Items);
    }

    private void UpdateRecentPlays(IEnumerable<RecentPlayTarget> targets)
    {
        RecentPlays.Clear();
        DisposeRecentPlays();
        _allRecentPlays = targets.Select(target => new RecentPlayItem(target)).ToList();
        SortRecentPlays();
        ApplyRecentPlayCapacity();
    }

    private void OnRecentPlaysRefreshed(object? sender, EventArgs e)
    {
        UpdateRecentPlays(_recentPlayListService.Items);
    }

    public void SetRecentPlayWidth(double width)
    {
        var capacity = Math.Max(1, (int)((width + 12) / 282));
        if (_recentPlayCapacity == capacity)
            return;

        _recentPlayCapacity = capacity;
        ApplyRecentPlayCapacity();
    }

    private void ApplyRecentPlayCapacity()
    {
        RecentPlays.Clear();
        foreach (var item in
                 _allRecentPlays.Take(_areRecentPlaysExpanded ? _allRecentPlays.Count : _recentPlayCapacity))
            RecentPlays.Add(item);
        OnPropertyChanged(nameof(HasRecentPlays));
        OnPropertyChanged(nameof(CanExpandRecentPlays));
        OnPropertyChanged(nameof(ToggleRecentPlaysText));
    }

    [RelayCommand]
    private void ToggleRecentPlays()
    {
        _areRecentPlaysExpanded = !_areRecentPlaysExpanded;
        ApplyRecentPlayCapacity();
    }

    public void ToggleRecentPlayFavorite(RecentPlayItem item)
    {
        item.ToggleFavorite();
        SortRecentPlays();
        ApplyRecentPlayCapacity();
    }

    private void SortRecentPlays() => _allRecentPlays = _allRecentPlays
        .OrderByDescending(item => item.IsFavorite)
        .ThenByDescending(item => item.LastPlayedTime)
        .ToList();

    public override void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _recentPlayListService.Refreshed -= OnRecentPlaysRefreshed;
        DisposeRecentPlays();
        RecentPlays.Clear();
        base.Dispose();
    }

    private void DisposeRecentPlays()
    {
        foreach (var item in _allRecentPlays)
            item.Dispose();
        _allRecentPlays.Clear();
    }
}

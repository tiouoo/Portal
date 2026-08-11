using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Services;
using Portal.Core.Operations;
using Portal.Core.Operations.OpenFile;
using Portal.Module.AggregatedSearch;
using Portal.Module.DefaultPage;
using Portal.Module.DesktopShortcut;
using Portal.Services;
using Portal.ViewModels;
using Portal.Views.Components;
using Portal.Views.Pages.DownloadPages;
using Portal.Views.Pages.InstancePages;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages;

[AggregatedSearchPage("新标签页", "新标签页", "NewTab")]
[DefaultPage("新标签页")]
public partial class NewTabPage : DataUserControl, ITioTabPage
{
    public NewTabViewModel NewTabViewModel;
    private bool _isInitialized;

    public NewTabPage()
    {
        InitializeComponent();
        NewTabViewModel = new NewTabViewModel();
        DataContext = NewTabViewModel;
        Loaded += async (_, _) =>
        {
            if (_isInitialized)
                return;

            _isInitialized = true;
            NewTabViewModel.ApplyFilterAndSort();
        };
        InstanceManager.Instance.StatisticsChanged += OnStatisticsChanged;
    }

    private void OnStatisticsChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(NewTabViewModel.UpdateStatistics);
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = "新标签页",
        Icon = StreamGeometry.Parse(
            "F1 M640,640z M0,0z M96.5,160L96.5,309.5C96.5,326.5,103.2,342.8,115.2,354.8L307.2,546.8C332.2,571.8,372.7,571.8,397.7,546.8L547.2,397.3C572.2,372.3,572.2,331.8,547.2,306.8L355.2,114.8C343.2,102.7,327,96,310,96L160.5,96C125.2,96,96.5,124.7,96.5,160z M208.5,176C226.2,176 240.5,190.3 240.5,208 240.5,225.7 226.2,240 208.5,240 190.8,240 176.5,225.7 176.5,208 176.5,190.3 190.8,176 208.5,176z")
    };

    public TabEntry HostTab { get; set; }

    public void OnClose()
    {
        InstanceManager.Instance.StatisticsChanged -= OnStatisticsChanged;
        NewTabViewModel.Dispose();
        DataContext = null;
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

    private void RecentInstanceCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is Visual visual && (visual is Button || visual.FindAncestorOfType<Button>() != null))
            return;

        if (sender is Control { Tag: MinecraftInstance instance } &&
            TopLevel.GetTopLevel(this) is { } topLevel)
            InstanceDetailPage.Open(instance, topLevel);
    }

    private void InputElement_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        ScrollViewer.Offset = new Vector(
            ScrollViewer.Offset.X + e.Delta.Y * -232,
            ScrollViewer.Offset.Y
        );
        e.Handled = true;
    }

    private void ContinueGame_Click(object? sender, RoutedEventArgs e)
    {
        if (NewTabViewModel.RecentInstance != null)
            _ = MinecraftLaunchService.LaunchAsync(NewTabViewModel.RecentInstance, TopLevel.GetTopLevel(this),
                MinecraftLaunchOptionsFactory.Create(NewTabViewModel.RecentInstance,
                    logSession => { MinecraftLogPage.Open(logSession, this.GetTopLevel()); }));
    }

    private void LaunchInstance_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not MinecraftInstance instance)
            return;

        _ = MinecraftLaunchService.LaunchAsync(instance, TopLevel.GetTopLevel(this),
            MinecraftLaunchOptionsFactory.Create(instance,
                logSession => MinecraftLogPage.Open(logSession, this.GetTopLevel())));
    }

    private async void CreateShortcut_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not MinecraftInstance instance)
            return;

        await DesktopShortcutUi.CreateAsync(TopLevel.GetTopLevel(this),
            () => DesktopShortcutService.CreateAsync(instance));
    }

    private void NewTabPage_OnSizeChanged(object? sender, SizeChangedEventArgs e) =>
        NewTabViewModel.SetRecentPlayWidth(e.NewSize.Width);

    private void QuickPlay_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not RecentPlayTarget target || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        _ = MinecraftLaunchService.LaunchAsync(target.Instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(target.Instance,
                logSession => MinecraftLogPage.Open(logSession, topLevel)), target);
    }

    private async void RecentPlayCreateShortcut_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not RecentPlayItem item)
            return;

        var target = item.Target;
        await DesktopShortcutUi.CreateAsync(TopLevel.GetTopLevel(this),
            () => DesktopShortcutService.CreateAsync(target.Instance, target));
    }

    private async void RecentPlayItem_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is Visual visual && (visual is Button || visual.FindAncestorOfType<Button>() != null))
            return;

        if (sender is not Control { DataContext: RecentPlayItem item })
            return;

        var target = item.Target;
        if (target.Type != RecentPlayTargetType.World)
            return;

        var saveService = new WorldSaveService();
        var worldInfo = await saveService.ReadAsync(target.Instance, target.Id);
        if (worldInfo == null)
            return;

        await OverlayDialog.ShowCustomAsync<WorldSaveDetails, WorldSaveDetailsViewModel, object>(
            new WorldSaveDetailsViewModel(worldInfo, target.Instance), this.TryGetHostId(),
            new OverlayDialogOptions
            {
                Mode = DialogMode.None,
                Buttons = DialogButton.None,
                CanLightDismiss = false,
                CanResize = false,
                IsCloseButtonVisible = true,
                CloseBtnMargin = new Thickness(0, 12, 12, 0)
            });
    }

    private void RecentPlayFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is RecentPlayItem item)
            NewTabViewModel.ToggleRecentPlayFavorite(item);
    }

    private async void RecentPlayTargetCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is Visual visual && (visual is Button || visual.FindAncestorOfType<Button>() != null))
            return;

        if (sender is not Control { Tag: RecentPlayTarget target })
            return;

        if (target.Type != RecentPlayTargetType.World)
            return;

        var saveService = new WorldSaveService();
        var worldInfo = await saveService.ReadAsync(target.Instance, target.Id);
        if (worldInfo == null)
            return;

        await OverlayDialog.ShowCustomAsync<WorldSaveDetails, WorldSaveDetailsViewModel, object>(
            new WorldSaveDetailsViewModel(worldInfo, target.Instance), this.TryGetHostId(),
            new OverlayDialogOptions
            {
                Mode = DialogMode.None,
                Buttons = DialogButton.None,
                CanLightDismiss = false,
                CanResize = false,
                IsCloseButtonVisible = true,
                CloseBtnMargin = new Thickness(0, 12, 12, 0)
            });
    }

    private void ContinueTargetGame_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not RecentPlayTarget target ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        _ = MinecraftLaunchService.LaunchAsync(target.Instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(target.Instance,
                logSession => MinecraftLogPage.Open(logSession, topLevel)), target);
    }

    private void FavoritedButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var instance = (sender as Control)?.Tag as MinecraftInstance;
        if (instance == null || instance.Config == null) return;

        instance.Config.IsFavorite = !instance.Config.IsFavorite;
        instance.SaveConfig();
        NewTabViewModel.ApplyFilterAndSort();
    }

    private void BlockInstance_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not MinecraftInstance instance)
            return;

        BlockListService.Instance.ToggleInstanceBlock(instance);
    }

    private void BlockRecentPlay_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not RecentPlayTarget target)
            return;

        BlockListService.Instance.ToggleRecentPlayBlock(target);
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

    private async Task RefreshInstancesAndRecentPlaysAsync()
    {
        InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
        NewTabViewModel.ApplyFilterAndSort();
        await RecentPlayListService.Instance.RefreshAsync();
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
}

public partial class NewTabViewModel : InstanceListViewModelBase
{
    private readonly RecentPlayListService _recentPlayListService = RecentPlayListService.Instance;
    private List<RecentPlayItem> _allRecentPlays = [];
    private int _recentPlayCapacity = 1;
    private bool _isDisposed;

    public NewTabViewModel()
    {
        SelectedSortOption = SortOptions.FirstOrDefault(o => o.SortType == Data.ConfigEntry.DefaultInstanceSortType);
        _recentPlayListService.Refreshed += OnRecentPlaysRefreshed;
        UpdateRecentPlays(_recentPlayListService.Items);
    }

    public NewsPage NewsPage { get; } = new(true);
    public ObservableCollection<RecentPlayItem> RecentPlays { get; } = [];
    public bool HasRecentPlays => RecentPlays.Count > 0;
    public bool CanExpandRecentPlays => GetVisibleRecentPlays().Count > _recentPlayCapacity;

    public string ToggleRecentPlaysText =>
        BlockListService.Instance.AreRecentPlaysExpanded ? "收起" : $"展开全部 ({GetVisibleRecentPlays().Count})";

    private RecentPlayItem? _recentPlayTargetItem;

    public RecentPlayItem? RecentPlayTargetItem
    {
        get => _recentPlayTargetItem;
        private set
        {
            if (SetProperty(ref _recentPlayTargetItem, value))
                OnPropertyChanged(nameof(HasRecentPlayTargetItem));
        }
    }

    public bool HasRecentPlayTargetItem => RecentPlayTargetItem != null;

    private List<RecentPlayItem> GetVisibleRecentPlays()
    {
        return BlockListService.Instance.ShowBlockedRecentPlays
            ? _allRecentPlays
            : _allRecentPlays.Where(item => !BlockListService.Instance.IsRecentPlayBlocked(item.Target)).ToList();
    }

    protected override void RefreshRecentPlaysForBlockList()
    {
        foreach (var item in _allRecentPlays)
            item.RefreshBlockState();
        ApplyRecentPlayCapacity();
        UpdateRecentPlayTargetItem();
    }

    protected override IEnumerable<RecentPlayTarget> GetRecentPlayTargets() =>
        _allRecentPlays.Select(item => item.Target);

    private void UpdateRecentPlays(IEnumerable<RecentPlayTarget> targets)
    {
        RecentPlays.Clear();
        DisposeRecentPlays();
        _allRecentPlays = targets.Select(target => new RecentPlayItem(target)).ToList();
        SortRecentPlays();
        ApplyRecentPlayCapacity();
        UpdateRecentPlayTargetItem();
    }

    private void UpdateRecentPlayTargetItem()
    {
        var visible = GetVisibleRecentPlays();
        RecentPlayTargetItem = visible.OrderByDescending(item => item.LastPlayedTime).FirstOrDefault();
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
        var visiblePlays = GetVisibleRecentPlays();
        var take = BlockListService.Instance.AreRecentPlaysExpanded ? visiblePlays.Count : _recentPlayCapacity;
        RecentPlays.Clear();
        foreach (var item in visiblePlays.Take(take))
            RecentPlays.Add(item);
        OnPropertyChanged(nameof(HasRecentPlays));
        OnPropertyChanged(nameof(CanExpandRecentPlays));
        OnPropertyChanged(nameof(ToggleRecentPlaysText));
    }

    [RelayCommand]
    private void ToggleRecentPlays()
    {
        BlockListService.Instance.AreRecentPlaysExpanded = !BlockListService.Instance.AreRecentPlaysExpanded;
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

    [RelayCommand]
    public void ToggleFavorite(MinecraftInstance instance)
    {
        if (instance == null || instance.Config == null) return;

        instance.Config.IsFavorite = !instance.Config.IsFavorite;
        instance.SaveConfig();
        ApplyFilterAndSort();
    }

    public override void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _recentPlayListService.Refreshed -= OnRecentPlaysRefreshed;
        DisposeRecentPlays();
        RecentPlays.Clear();
        NewsPage.DataContext = null;
        base.Dispose();
    }

    private void DisposeRecentPlays()
    {
        RecentPlayTargetItem = null;
        foreach (var item in _allRecentPlays)
            item.Dispose();
        _allRecentPlays.Clear();
    }
}

public sealed class RecentPlayItem : INotifyPropertyChanged, IDisposable
{
    private readonly RecentPlayTarget _target;
    private Bitmap? _ownedIcon;
    private bool _iconLoaded;

    public RecentPlayItem(RecentPlayTarget target) => _target = target;

    public event PropertyChangedEventHandler? PropertyChanged;

    public RecentPlayTarget Target => _target;
    public string Name => _target.Name;
    public string InstanceName => _target.Instance.InstanceName;
    public string Details => _target.Details;
    public DateTime LastPlayedTime => _target.LastPlayedTime;
    public string RelativeTime => GetRelativeTime(_target.LastPlayedTime);
    public bool CanQuickPlay => _target.CanQuickPlay;

    public string? FolderName => _target.Type == RecentPlayTargetType.World
        ? _target.Id
        : null;

    public bool HasFolderName => FolderName is not null;

    public bool IsFavorite =>
        _target.Instance.Config.RecentPlayFavorites?.TryGetValue(_target.Id, out var favorite) == true && favorite;

    public bool IsBlocked => BlockListService.Instance.IsRecentPlayBlocked(_target);
    public string BlockHeaderText => IsBlocked ? "取消屏蔽" : "屏蔽";
    public string FavoriteHeaderText => IsFavorite ? "取消收藏" : "收藏";

    public Bitmap Icon
    {
        get
        {
            if (!_iconLoaded)
            {
                _iconLoaded = true;
                _ownedIcon = _target.Type == RecentPlayTargetType.Server && _target.ServerIconData is { Length: > 0 }
                    ? LoadIcon(_target.ServerIconData)
                    : _target.WorldIconPath is { } path && File.Exists(path)
                        ? LoadIcon(path)
                        : null;
            }

            return _ownedIcon ?? _target.Instance.Icon;
        }
    }

    public void ToggleFavorite()
    {
        var favorites = _target.Instance.Config.RecentPlayFavorites ??= [];
        if (IsFavorite)
            favorites.Remove(_target.Id);
        else
            favorites[_target.Id] = true;
        _target.Instance.SaveConfig();
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteHeaderText));
    }

    public void RefreshBlockState()
    {
        OnPropertyChanged(nameof(IsBlocked));
        OnPropertyChanged(nameof(BlockHeaderText));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static Bitmap? LoadIcon(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data);
            return Bitmap.DecodeToWidth(stream, 48);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Bitmap? LoadIcon(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, 48);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        var icon = _ownedIcon;
        _ownedIcon = null;
        if (icon != null)
            Dispatcher.UIThread.Post(icon.Dispose, DispatcherPriority.Background);
    }

    private static string GetRelativeTime(DateTime time)
    {
        var elapsed = DateTime.Now - time;
        if (elapsed.TotalMinutes < 1) return "刚刚";
        if (elapsed.TotalDays >= 30) return time.ToString("yyyy-MM-dd HH:mm");
        if (elapsed.TotalDays >= 1) return $"{(int)elapsed.TotalDays} 天前";
        return elapsed.TotalHours >= 1 ? $"{(int)elapsed.TotalHours} 小时前" : $"{(int)elapsed.TotalMinutes} 分钟前";
    }
}
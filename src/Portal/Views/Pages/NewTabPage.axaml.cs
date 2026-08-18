using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Services;
using Portal.Core.Const;
using Portal.Core.Module.AggregatedSearch;
using Portal.Core.Services;
using Portal.Module.DefaultPage;
using Portal.ViewModels;
using Portal.Views.Pages.InstancePages;
using Tio.Avalonia.Standard.Tab.Entries;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages;

[AggregatedSearchPage("新标签页", "新标签页", "NewTab")]
[DefaultPage("新标签页")]
public partial class NewTabPage : InstanceListPageBase
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

    public override PageInfo PageInfo { get; init; } = new()
    {
        Title = "新标签页",
        Icon = StreamGeometry.Parse(
            "F1 M640,640z M0,0z M96.5,160L96.5,309.5C96.5,326.5,103.2,342.8,115.2,354.8L307.2,546.8C332.2,571.8,372.7,571.8,397.7,546.8L547.2,397.3C572.2,372.3,572.2,331.8,547.2,306.8L355.2,114.8C343.2,102.7,327,96,310,96L160.5,96C125.2,96,96.5,124.7,96.5,160z M208.5,176C226.2,176 240.5,190.3 240.5,208 240.5,225.7 226.2,240 208.5,240 190.8,240 176.5,225.7 176.5,208 176.5,190.3 190.8,176 208.5,176z")
    };

    protected override InstanceListViewModelBase PageViewModel => NewTabViewModel;

    public override void OnClose()
    {
        InstanceManager.Instance.StatisticsChanged -= OnStatisticsChanged;
        base.OnClose();
    }

    private void OnStatisticsChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(NewTabViewModel.UpdateStatistics);
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
}

public partial class NewTabViewModel : RecentPlaysViewModelBase
{
    private RecentPlayItem? _recentPlayTargetItem;

    public NewTabViewModel()
    {
        SelectedSortOption = SortOptions.FirstOrDefault(o => o.SortType == Data.ConfigEntry.DefaultInstanceSortType);
    }

    public NewsPage NewsPage { get; } = new(true);

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

    protected override bool RecentPlaysExpanded
    {
        get => Data.ConfigEntry.NewTabRecentPlaysExpanded;
        set => Data.ConfigEntry.NewTabRecentPlaysExpanded = value;
    }

    protected override void RefreshRecentPlaysForBlockList()
    {
        base.RefreshRecentPlaysForBlockList();
        UpdateRecentPlayTargetItem();
    }

    protected override void UpdateRecentPlays(IEnumerable<RecentPlayTarget> targets)
    {
        base.UpdateRecentPlays(targets);
        UpdateRecentPlayTargetItem();
    }

    private void UpdateRecentPlayTargetItem()
    {
        var visible = GetVisibleRecentPlays();
        RecentPlayTargetItem = visible.OrderByDescending(item => item.LastPlayedTime).FirstOrDefault();
    }

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
        NewsPage.DataContext = null;
        base.Dispose();
    }

    protected override void DisposeRecentPlays()
    {
        RecentPlayTargetItem = null;
        base.DisposeRecentPlays();
    }
}

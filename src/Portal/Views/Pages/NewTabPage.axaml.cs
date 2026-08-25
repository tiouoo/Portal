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
using Portal.Core.Module;
using Portal.Module.DefaultPage;
using Portal.Localization;
using Portal.ViewModels;
using Portal.Views.Pages.InstancePages;
using Tio.Avalonia.Standard.Tab.Entries;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

using Portal.Module;
namespace Portal.Views.Pages;

[AggregatedSearchPage("pages_newTab", "pages_newTabPath", "NewTab")]
[DefaultPage("pages_newTab")]
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
        Title = CommonLanguageManager.Instance.newTabPage_pageTitle.CurrentValue(),
        IconGlyph = "\ue619", IconFont = IconResources.FontFamilyName
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

    private void RecentInstanceLaunch_Click(object? sender, RoutedEventArgs e)
    {
        ContinueGame_Click(sender, e);
    }

    private async void RecentInstanceCreateShortcut_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is MinecraftInstance instance)
            await DesktopShortcutUi.CreateAsync(TopLevel.GetTopLevel(this),
                () => DesktopShortcutService.CreateAsync(instance));
    }

    private void RecentInstanceFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is MinecraftInstance instance)
            NewTabViewModel.ToggleFavorite(instance);
    }

    private void RecentInstanceBlock_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is MinecraftInstance instance)
            BlockListService.Instance.ToggleInstanceBlock(instance);
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

    private async void RecentTargetCreateShortcut_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is RecentPlayItem item)
            await DesktopShortcutUi.CreateAsync(TopLevel.GetTopLevel(this),
                () => DesktopShortcutService.CreateAsync(item.Target.Instance, item.Target));
    }

    private void RecentTargetFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is RecentPlayItem item)
            NewTabViewModel.ToggleRecentPlayFavorite(item);
    }

    private void RecentTargetBlock_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is RecentPlayTarget target)
            BlockListService.Instance.ToggleRecentPlayBlock(target);
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

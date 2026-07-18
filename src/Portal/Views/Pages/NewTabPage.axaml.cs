using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Operations;
using Portal.Module.AggregatedSearch;
using Portal.Module.DefaultPage;
using Portal.Services;
using Portal.ViewModels;
using Portal.Views.Components;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;
using RandomMinecraft = Portal.Views.Components.RandomMinecraft;

namespace Portal.Views.Pages;

[AggregatedSearchPage("新标签页", "新标签页", "NewTab")]
[DefaultPage("新标签页")]
public partial class NewTabPage : DataUserControl, ITioTabPage
{
    public NewTabViewModel NewTabViewModel;

    public NewTabPage()
    {
        InitializeComponent();
        NewTabViewModel = new NewTabViewModel();
        DataContext = NewTabViewModel;
        Loaded += (_, _) => { NewTabViewModel.ApplyFilterAndSort(); };
        InstanceManager.Instance.StatisticsChanged += OnStatisticsChanged;
        Unloaded += (_, _) => InstanceManager.Instance.StatisticsChanged -= OnStatisticsChanged;
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
                MinecraftLaunchOptionsFactory.Create(logSession =>
                {
                    MinecraftLogPage.Open(logSession, this.GetTopLevel());
                }));
    }

    private void LaunchInstance_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not MinecraftInstance instance)
            return;

        _ = MinecraftLaunchService.LaunchAsync(instance, TopLevel.GetTopLevel(this),
            MinecraftLaunchOptionsFactory.Create(logSession => MinecraftLogPage.Open(logSession, this.GetTopLevel())));
    }

    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        if (InstanceManager.Instance.Instances.Count == 0)
        {
            if (sender is not null)
                sender.AsTopLevel()?.Notice("还没有实例可以抽签哦", NotificationType.Error);
            return;
        }

        OverlayDialogOptions options = new()
        {
            Title = "开奖啦！",
            IsCloseButtonVisible = false,
            Buttons = DialogButton.YesNoCancel,
            OverrideNoButtonText = "再来亿次",
            OverrideYesButtonText = "就它了",
            OverrideCancelButtonText = "不玩了",
            CanLightDismiss = false,
            CanDragMove = true,
            CanResize = true,
            VerticalAnchor = VerticalPosition.Top,
            VerticalOffset = 110,
        };
        _ = Show();

        return;

        async Task Show()
        {
            var result = InstanceManager.Instance.Instances.OrderBy(x => Guid.NewGuid()).FirstOrDefault();
            if (result is null || sender is null || sender.AsTopLevel() is not { } topLevel)
                return;

            var feed = await OverlayDialog.ShowCustomAsync<RandomMinecraft, RandomMinecraftViewModle, string>(
                new RandomMinecraftViewModle(result), topLevel.TryGetHostId(), options: options);

            if (feed == "again")
            {
                _ = Show();
                return;
            }

            if (feed == "yes")
                _ = MinecraftLaunchService.LaunchAsync(result, topLevel,
                    MinecraftLaunchOptionsFactory.Create(logSession =>
                    {
                        MinecraftLogPage.Open(logSession, this.GetTopLevel());
                    }));
        }
    }

    private void FavoritedButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var instance = (sender as Control)?.Tag as MinecraftInstance;
        if (instance == null || instance.Config == null) return;

        instance.Config.IsFavorite = !instance.Config.IsFavorite;
        instance.SaveConfig();
        NewTabViewModel.ApplyFilterAndSort();
    }

    private void ButtonOpenInstance_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is null || sender.AsTopLevel() is not TioTabWindowBase window)
            return;

        var tab = new TabEntry(window, new InstancesPage());
        window.CreateTab(tab);
        window.SelectTab(tab);
    }

    private void RefreshInstance_Click(object? sender, RoutedEventArgs e)
    {
        InstanceManager.Instance.RefreshAll(
            Data.ConfigEntry.MinecraftFolders.Select(f => (f.FolderPath, f.FolderName))
        );
        NewTabViewModel.ApplyFilterAndSort();
    }
}

public partial class NewTabViewModel : InstanceListViewModelBase
{
    public NewTabViewModel()
    {
        SelectedSortOption = SortOptions.FirstOrDefault(o => o.SortType == Data.ConfigEntry.DefaultInstanceSortType);
    }

    public NewsPage NewsPage { get; } = new(true);

    [RelayCommand]
    public void ToggleFavorite(MinecraftInstance instance)
    {
        if (instance == null || instance.Config == null) return;

        instance.Config.IsFavorite = !instance.Config.IsFavorite;
        instance.SaveConfig();
        ApplyFilterAndSort();
    }
}

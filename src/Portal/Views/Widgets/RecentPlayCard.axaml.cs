using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using Portal.Core.Module;
using Portal.Core.Services;
using Portal.Views.Pages;
using Portal.Views.Pages.InstancePages;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Widgets;

public partial class RecentPlayCard : UserControl
{
    public static readonly StyledProperty<bool> ShowQuickPlayWhenPossibleProperty =
        AvaloniaProperty.Register<RecentPlayCard, bool>(nameof(ShowQuickPlayWhenPossible), true);

    public static readonly RoutedEvent<RoutedEventArgs> FavoriteChangedEvent =
        RoutedEvent.Register<RecentPlayCard, RoutedEventArgs>(nameof(FavoriteChangedEvent), RoutingStrategies.Bubble);

    public RecentPlayCard()
    {
        InitializeComponent();
    }

    public bool ShowQuickPlayWhenPossible
    {
        get => GetValue(ShowQuickPlayWhenPossibleProperty);
        set => SetValue(ShowQuickPlayWhenPossibleProperty, value);
    }

    private void ContextMenu_OnOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        var visible = !ShowQuickPlayWhenPossible || DataContext is RecentPlayItem { CanQuickPlay: true };
        foreach (var item in menu.Items)
        {
            if (item is MenuItem { Header: string header } menuItem && header == "游玩")
            {
                menuItem.IsVisible = visible;
                break;
            }
        }
    }

    private void QuickPlay_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RecentPlayItem { Target: { } target } || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        _ = MinecraftLaunchService.LaunchAsync(target.Instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(target.Instance,
                logSession => MinecraftLogPage.Open(logSession, topLevel)), target);
    }

    private async void RecentPlayCreateShortcut_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RecentPlayItem item)
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

        if (DataContext is not RecentPlayItem item)
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
        if (DataContext is RecentPlayItem)
            RaiseEvent(new RoutedEventArgs(FavoriteChangedEvent));
    }

    private void BlockRecentPlay_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RecentPlayItem item)
            return;

        BlockListService.Instance.ToggleRecentPlayBlock(item.Target);
    }
}

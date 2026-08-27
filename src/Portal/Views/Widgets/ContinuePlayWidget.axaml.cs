using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Services;
using Portal.Core.Module.Widgets;
using Portal.Views.Pages;
using Portal.Views.Pages.InstancePages;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Widgets;

public partial class ContinuePlayWidget : IWidgetContent, INotifyPropertyChanged
{
    private RecentPlayItem? _item;
    private int _refreshVersion;

    public ContinuePlayWidget()
    {
        Size = new WidgetCellSize(2, 1);
        InitializeComponent();
        DataContext = this;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public RecentPlayItem? Item
    {
        get => _item;
        private set
        {
            if (ReferenceEquals(_item, value)) return;
            _item?.Dispose();
            _item = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Item)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasItem)));
        }
    }

    public bool HasItem => Item != null;
    public new event PropertyChangedEventHandler? PropertyChanged;

    private void OnLoaded(object? sender, EventArgs e)
    {
        InstanceManager.Instance.InstancesChanged += OnSourceChanged;
        RecentPlayListService.Instance.Refreshed += OnSourceChanged;
        _ = RefreshAsync();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        InstanceManager.Instance.InstancesChanged -= OnSourceChanged;
        RecentPlayListService.Instance.Refreshed -= OnSourceChanged;
        Item = null;
    }

    private void OnSourceChanged(object? sender, EventArgs e) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        var version = ++_refreshVersion;
        var target = (await new RecentPlayService().ScanAsync(InstanceManager.Instance.Instances))
            .OrderByDescending(item => item.LastPlayedTime)
            .FirstOrDefault();
        if (version == _refreshVersion)
            Item = target == null ? null : new RecentPlayItem(target);
    }

    public override async void PerformClick()
    {
        if (Item?.Target is not { Type: RecentPlayTargetType.World } target)
            return;
        var world = await new WorldSaveService().ReadAsync(target.Instance, target.Id);
        if (world == null) return;
        await OverlayDialog.ShowCustomAsync<WorldSaveDetails, WorldSaveDetailsViewModel, object>(
            new WorldSaveDetailsViewModel(world, target.Instance), this.TryGetHostId(), new OverlayDialogOptions
            {
                Mode = DialogMode.None,
                Buttons = DialogButton.None,
                CanLightDismiss = false,
                CanResize = false,
                IsCloseButtonVisible = true,
                CloseBtnMargin = new Thickness(0, 12, 12, 0)
            });
    }

    private void ContinueGame_Click(object? sender, RoutedEventArgs e)
    {
        if (Item?.Target is not { } target || TopLevel.GetTopLevel(this) is not { } topLevel) return;
        _ = MinecraftLaunchService.LaunchAsync(target.Instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(target.Instance,
                logSession => MinecraftLogPage.Open(logSession, topLevel)), target);
    }
}

using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Tio.Avalonia.Standard.Tab.Gateway;
using Tio.Avalonia.Standard.Modules.Tasks;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages;

public partial class MultiplayerContentPage : UserControl
{
    private MultiplayerPageViewModel _viewModel = null!;

    public MultiplayerContentPage()
    {
        InitializeComponent();
    }

    public MultiplayerContentPage(MultiplayerPageViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private async void CopyRoomCode_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (string.IsNullOrWhiteSpace(_viewModel.CurrentRoomCode) || topLevel?.Clipboard is not { } clipboard)
            return;

        await clipboard.SetTextAsync(_viewModel.CurrentRoomCode);
        NotificationGateway.Notice(topLevel, "房间码已复制", NotificationType.Success);
    }

    private async void PasteJoinCode_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        _viewModel.JoinCode = await clipboard.TryGetTextAsync() ?? string.Empty;
    }

    private async void EnterJavaPort_OnClick(object? sender, RoutedEventArgs e)
    {
        var result = await OverlayDialog.ShowCustomAsync<MultiplayerPortDialog, MultiplayerPortDialogViewModel, string>(
            new MultiplayerPortDialogViewModel(_viewModel.ManualJavaPort), this.GetTopLevel().TryGetHostId(),
            new OverlayDialogOptions
            {
                Title = "请输入局域网端口", Buttons = DialogButton.None, CanLightDismiss = false, CanResize = false
            });
        if (result is not null) await _viewModel.CreateJavaRoomFromPortAsync(result);
    }
}

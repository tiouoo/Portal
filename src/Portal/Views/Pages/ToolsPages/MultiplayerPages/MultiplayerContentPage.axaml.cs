using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Portal.Localization;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.ToolsPages.MultiplayerPages;

public partial class MultiplayerContentPage : UserControl
{
    private readonly MultiplayerPageViewModel _viewModel = null!;

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
        topLevel.Notice(CommonLanguageManager.Instance.multiplayer_roomCodeCopied.CurrentValue(),
            NotificationType.Success);
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
                Title = CommonLanguageManager.Instance.multiplayer_enterLanPort.CurrentValue(), Buttons = DialogButton.None,
                CanLightDismiss = false, CanResize = false
            });
        if (result is not null) await _viewModel.CreateJavaRoomFromPortAsync(result);
    }
}
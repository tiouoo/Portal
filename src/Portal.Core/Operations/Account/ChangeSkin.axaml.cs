using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TioUi.Common;
using TioUi.Common.Interfaces;
using TioUi.Controls;

namespace Portal.Core.Operations.Account;

public partial class ChangeSkin : UserControl
{
    public ChangeSkin()
    {
        InitializeComponent();
    }
}

public static class ChangeSkinDialog
{
    public static async Task<string?> Show(string? hostId, string? currentSkinPath)
    {
        var options = new OverlayDialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            CanResize = false,
            VerticalOffset = 80,
            VerticalAnchor = VerticalPosition.Top
        };

        var result = await OverlayDialog.ShowCustomAsync<ChangeSkin, ChangeSkinViewModel, string?>(
            new ChangeSkinViewModel(currentSkinPath), hostId: hostId, options: options);

        return result;
    }
}

public partial class ChangeSkinViewModel : ObservableObject, IDialogContext
{
    [ObservableProperty]
    public partial string? SkinPath { get; set; }

    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    public ChangeSkinViewModel(string? currentSkinPath)
    {
        SkinPath = currentSkinPath;
        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
        CancelCommand = new RelayCommand(Cancel);
    }

    partial void OnSkinPathChanged(string? value)
    {
        (ConfirmCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private bool CanConfirm()
    {
        return !string.IsNullOrEmpty(SkinPath) && System.IO.File.Exists(SkinPath);
    }

    private void Confirm()
    {
        RequestClose?.Invoke(this, SkinPath);
    }

    private void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }

    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;
}

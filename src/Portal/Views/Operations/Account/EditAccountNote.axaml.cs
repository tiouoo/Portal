using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TioUi.Common;
using TioUi.Common.Interfaces;
using TioUi.Controls;

namespace Portal.Views.Operations.Account;

public partial class EditAccountNote : UserControl
{
    public EditAccountNote()
    {
        InitializeComponent();
    }
}

public static class EditAccountNoteDialog
{
    public static async Task<string?> Show(string hostId, string? accountNote)
    {
        var options = new OverlayDialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            CanResize = false,
            VerticalAnchor = VerticalPosition.Top,
            VerticalOffset = 110
        };

        var result = await OverlayDialog.ShowCustomAsync<EditAccountNote, EditAccountNoteViewModel, string?>(
            new EditAccountNoteViewModel(accountNote), hostId, options);

        return result;
    }
}

public partial class EditAccountNoteViewModel : ObservableObject, IDialogContext
{
    public EditAccountNoteViewModel(string? accountNote)
    {
        AccountNote = accountNote;
        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
        CancelCommand = new RelayCommand(Cancel);
    }

    [ObservableProperty] public partial string? AccountNote { get; set; }

    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;

    partial void OnAccountNoteChanged(string? value)
    {
        (ConfirmCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private bool CanConfirm()
    {
        return true;
    }

    private void Confirm()
    {
        RequestClose?.Invoke(this, AccountNote);
    }

    private void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }
}
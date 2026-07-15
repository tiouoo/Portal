using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Helpers;
using Portal.Core.Minecraft.Classes;
using TioUi.Common;
using TioUi.Common.Interfaces;
using TioUi.Controls;

namespace Portal.Core.Operations.Account;

public partial class EditAccountNote : UserControl
{
    public EditAccountNote()
    {
        InitializeComponent();
    }
}

public static class EditAccountNoteDialog
{
    public static async Task<string?> Show(string hostId, string accountNote)
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

        var result = await OverlayDialog.ShowCustomAsync<EditAccountNote, EditAccountNoteViewModel, string?>(
            new EditAccountNoteViewModel(accountNote), hostId: hostId, options: options);

        return result;
    }
}

public partial class EditAccountNoteViewModel : ObservableObject, IDialogContext
{
    [ObservableProperty]
    public partial string? AccountNote { get; set; }
    
    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }


    public EditAccountNoteViewModel(string accountNote)
    {
        AccountNote = accountNote;
        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
        CancelCommand = new RelayCommand(Cancel);
    }

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

    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;
}
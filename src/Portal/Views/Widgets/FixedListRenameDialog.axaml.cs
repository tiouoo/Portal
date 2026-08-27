using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TioUi.Common.Interfaces;

namespace Portal.Views.Widgets;

public partial class FixedListRenameDialog : UserControl
{
    public FixedListRenameDialog()
    {
        InitializeComponent();
    }
}

public partial class FixedListRenameDialogViewModel(string currentName) : ObservableObject, IDialogContext
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _name = currentName;

    public void Close() => Cancel();

    public event EventHandler<object?>? RequestClose;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        RequestClose?.Invoke(this, Name.Trim());
    }

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(Name);

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }
}

using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TioUi.Common.Interfaces;

namespace Portal.Views.Pages;

public partial class MultiplayerPortDialog : UserControl
{
    public MultiplayerPortDialog() => InitializeComponent();
}

public partial class MultiplayerPortDialogViewModel(string port) : ObservableObject, IDialogContext
{
    [ObservableProperty] public partial string Port { get; set; } = port;

    [RelayCommand]
    private void Confirm() => RequestClose?.Invoke(this, Port.Trim());

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(this, null);

    public void Close() => Cancel();
    public event EventHandler<object?>? RequestClose;
}

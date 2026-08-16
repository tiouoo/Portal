using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TioUi.Common.Interfaces;

namespace Portal.Core.Operations.Account;

public partial class AuthServerDetected : UserControl
{
    public AuthServerDetected()
    {
        InitializeComponent();
    }
}

public enum AuthServerDetectedAction
{
    Cancel,
    AddServer,
    Login
}

public partial class AuthServerDetectedViewModel : ObservableObject, IDialogContext
{
    public AuthServerDetectedViewModel(string serverUrl)
    {
        ServerUrl = serverUrl;
        CancelCommand = new RelayCommand(Cancel);
        AddServerCommand = new RelayCommand(AddServer);
        LoginCommand = new RelayCommand(Login);
    }

    [ObservableProperty] public partial string? ServerUrl { get; set; }

    public ICommand CancelCommand { get; }
    public ICommand AddServerCommand { get; }
    public ICommand LoginCommand { get; }

    public void Close()
    {
        RequestClose?.Invoke(this, AuthServerDetectedAction.Cancel);
    }

    public event EventHandler<object?>? RequestClose;

    private void Cancel()
    {
        RequestClose?.Invoke(this, AuthServerDetectedAction.Cancel);
    }

    private void AddServer()
    {
        RequestClose?.Invoke(this, AuthServerDetectedAction.AddServer);
    }

    private void Login()
    {
        RequestClose?.Invoke(this, AuthServerDetectedAction.Login);
    }
}
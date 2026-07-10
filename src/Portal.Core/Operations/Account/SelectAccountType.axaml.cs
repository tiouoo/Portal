using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft.Account;
using TioUi.Common;
using TioUi.Controls;
using TioUi.Common.Interfaces;

namespace Portal.Core.Operations.Account;

public partial class SelectAccountType : UserControl
{
    public SelectAccountType()
    {
        InitializeComponent();
    }
}

public partial class SelectAccountTypeViewModel : ObservableObject, IDialogContext
{
    public ObservableCollection<Minecraft.Account.AuthServer> AuthServers { get; } = [];

    private Minecraft.Account.AuthServer? _selectedServer;
    public Minecraft.Account.AuthServer? SelectedServer
    {
        get => _selectedServer;
        set
        {
            SetProperty(ref _selectedServer, value);
            (NextCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    public ICommand NextCommand { get; }
    public ICommand CancelCommand { get; }


    public SelectAccountTypeViewModel()
    {
        AuthServers.Add(new Minecraft.Account.AuthServer(AccountType.Offline, "离线模式"));
        AuthServers.Add(new Minecraft.Account.AuthServer(AccountType.Microsoft, "微软账户"));
        AuthServers.Add(new Minecraft.Account.AuthServer(AccountType.Yggdrasil, "外置登录"));

        NextCommand = new RelayCommand(Next, CanNext);
        CancelCommand = new RelayCommand(Cancel);

        SelectedServer = AuthServers.FirstOrDefault();
    }



    private bool CanNext()
    {
        return SelectedServer != null;
    }

    private void Next()
    {
        RequestClose?.Invoke(this, new SelectAccountTypeResult(SelectAccountTypeAction.Select, SelectedServer));
    }

    private void Cancel()
    {
        RequestClose?.Invoke(this, new SelectAccountTypeResult(SelectAccountTypeAction.Cancel));
    }

    public void Close()
    {
        RequestClose?.Invoke(this, new SelectAccountTypeResult(SelectAccountTypeAction.Cancel));
    }

    public event EventHandler<object?>? RequestClose;
}

public enum SelectAccountTypeAction
{
    Cancel,
    Select
}

public class SelectAccountTypeResult
{
    public SelectAccountTypeAction Action { get; }
    public Minecraft.Account.AuthServer? SelectedServer { get; }

    public SelectAccountTypeResult(SelectAccountTypeAction action, Minecraft.Account.AuthServer? selectedServer = null)
    {
        Action = action;
        SelectedServer = selectedServer;
    }
}
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft.Classes;
using TioUi.Common.Interfaces;

namespace Portal.Core.Operations.Account;

public partial class SelectAccountType : UserControl
{
    public SelectAccountType()
    {
        InitializeComponent();
    }
}

public class SelectAccountTypeViewModel : ObservableObject, IDialogContext
{
    private Minecraft.Classes.AuthServer? _selectedServer;


    public SelectAccountTypeViewModel()
    {
        AuthServers.Add(new Minecraft.Classes.AuthServer(AccountType.Offline, "离线模式"));
        AuthServers.Add(new Minecraft.Classes.AuthServer(AccountType.Microsoft, "微软账户"));
        AuthServers.Add(new Minecraft.Classes.AuthServer(AccountType.Yggdrasil, "外置登录"));
        if (!OperatingSystem.IsMacOS())
            AuthServers.Add(new Minecraft.Classes.AuthServer(AccountType.Bedrock, "关联Xbox"));

        NextCommand = new RelayCommand(Next, CanNext);
        CancelCommand = new RelayCommand(Cancel);

        SelectedServer = AuthServers.FirstOrDefault();
    }

    public ObservableCollection<Minecraft.Classes.AuthServer> AuthServers { get; } = [];

    public Minecraft.Classes.AuthServer? SelectedServer
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

    public void Close()
    {
        RequestClose?.Invoke(this, new SelectAccountTypeResult(SelectAccountTypeAction.Cancel));
    }

    public event EventHandler<object?>? RequestClose;


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
}

public enum SelectAccountTypeAction
{
    Cancel,
    Select
}

public class SelectAccountTypeResult
{
    public SelectAccountTypeResult(SelectAccountTypeAction action, Minecraft.Classes.AuthServer? selectedServer = null)
    {
        Action = action;
        SelectedServer = selectedServer;
    }

    public SelectAccountTypeAction Action { get; }
    public Minecraft.Classes.AuthServer? SelectedServer { get; }
}
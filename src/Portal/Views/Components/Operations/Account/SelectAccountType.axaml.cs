using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft.Classes;
using Portal.Localization;
using TioUi.Common.Interfaces;
using Minecraft = Portal.Core.Minecraft;

namespace Portal.Views.Components.Operations.Account;

public partial class SelectAccountType : UserControl
{
    public SelectAccountType()
    {
        InitializeComponent();
    }

    private void AccountType_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Control).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed ||
            (sender as Control)?.DataContext is not Minecraft.Classes.AuthServer server ||
            DataContext is not SelectAccountTypeViewModel viewModel)
            return;

        viewModel.Select(server);
    }
}

public class SelectAccountTypeViewModel : ObservableObject, IDialogContext
{
    private Minecraft.Classes.AuthServer? _selectedServer;


    public SelectAccountTypeViewModel()
    {
        OfflineServer = new Minecraft.Classes.AuthServer(AccountType.Offline,
            CommonLanguageManager.Instance.account_offlineMode.CurrentValue()) { IconGlyph = "\ue63e" };
        MicrosoftServer = new Minecraft.Classes.AuthServer(AccountType.Microsoft,
            CommonLanguageManager.Instance.account_microsoft.CurrentValue()) { IconGlyph = "\ue656" };
        YggdrasilServer = new Minecraft.Classes.AuthServer(AccountType.Yggdrasil,
            CommonLanguageManager.Instance.account_yggdrasil.CurrentValue()) { IconGlyph = "\ue614" };
        AuthServers.Add(OfflineServer);
        AuthServers.Add(MicrosoftServer);
        AuthServers.Add(YggdrasilServer);
        if (!OperatingSystem.IsMacOS())
        {
            BedrockServer = new Minecraft.Classes.AuthServer(AccountType.Bedrock,
                CommonLanguageManager.Instance.account_linkXbox.CurrentValue()) { IconGlyph = "\ue655" };
            AuthServers.Add(BedrockServer);
        }

        NextCommand = new RelayCommand(Next, CanNext);
        CancelCommand = new RelayCommand(Cancel);

        if (AuthServers.FirstOrDefault() is { } firstServer)
            Select(firstServer);
    }

    public ObservableCollection<Minecraft.Classes.AuthServer> AuthServers { get; } = [];

    public Minecraft.Classes.AuthServer OfflineServer { get; }
    public Minecraft.Classes.AuthServer MicrosoftServer { get; }
    public Minecraft.Classes.AuthServer YggdrasilServer { get; }
    public Minecraft.Classes.AuthServer? BedrockServer { get; }
    public bool HasBedrockServer => BedrockServer != null;

    public Minecraft.Classes.AuthServer? SelectedServer
    {
        get => _selectedServer;
        set
        {
            SetProperty(ref _selectedServer, value);
            (NextCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    public void Select(Minecraft.Classes.AuthServer server)
    {
        foreach (var authServer in AuthServers)
            authServer.IsSelected = authServer == server;
        SelectedServer = server;
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

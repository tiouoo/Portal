using System.Collections;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Helpers;
using Portal.Core.Minecraft.Classes;
using Portal.Localization;
using TioUi.Common.Interfaces;
using Minecraft = Portal.Core.Minecraft;

namespace Portal.Views.Components.Operations.Account;

public partial class AuthServer : UserControl
{
    public AuthServer()
    {
        InitializeComponent();
    }
}

public partial class AuthServerViewModel : ObservableObject, IDialogContext, INotifyDataErrorInfo
{
    private readonly Dictionary<string, List<string>> _errors = new();
    private readonly Minecraft.Classes.AuthServer[] _existingServers;

    public AuthServerViewModel(Minecraft.Classes.AuthServer[] existingServers)
    {
        _existingServers = existingServers;
        NextCommand = new RelayCommand(Next, CanNext);
        CancelCommand = new RelayCommand(Cancel);
    }

    [ObservableProperty] public partial string? ServerName { get; set; }

    [ObservableProperty] public partial string? ServerUrl { get; set; }

    public ICommand NextCommand { get; }
    public ICommand CancelCommand { get; }

    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;

    public bool HasErrors => _errors.Count > 0;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName) || !_errors.ContainsKey(propertyName)) return Enumerable.Empty<string>();
        return _errors[propertyName];
    }

    partial void OnServerNameChanged(string? value)
    {
        ValidateServerName(value);
        (NextCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    partial void OnServerUrlChanged(string? value)
    {
        ValidateServerUrl(value);
        (NextCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void ValidateServerName(string? value)
    {
        var propertyName = nameof(ServerName);

        if (_errors.ContainsKey(propertyName)) _errors.Remove(propertyName);

        if (string.IsNullOrWhiteSpace(value))
            _errors[propertyName] =
                new List<string> { CommonLanguageManager.Instance.account_serverNameEmpty.CurrentValue() };

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }

    private void ValidateServerUrl(string? value)
    {
        var propertyName = nameof(ServerUrl);

        if (_errors.ContainsKey(propertyName)) _errors.Remove(propertyName);

        if (string.IsNullOrWhiteSpace(value))
            _errors[propertyName] =
                new List<string> { CommonLanguageManager.Instance.account_serverUrlEmpty.CurrentValue() };
        else if (!UrlHelper.IsValidUrl(value))
            _errors[propertyName] =
                new List<string> { CommonLanguageManager.Instance.account_urlInvalid.CurrentValue() };
        else if (IsUrlExists(value))
            _errors[propertyName] =
                new List<string> { CommonLanguageManager.Instance.account_authServerExists.CurrentValue() };

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }

    private bool IsUrlExists(string url)
    {
        return _existingServers.Any(server =>
            server.AuthType == AccountType.Yggdrasil &&
            !string.IsNullOrEmpty(server.ServerUrl) &&
            UrlHelper.AreUrlsEqual(server.ServerUrl, url));
    }

    private bool CanNext()
    {
        return !HasErrors && !string.IsNullOrWhiteSpace(ServerName) && !string.IsNullOrWhiteSpace(ServerUrl);
    }

    private void Next()
    {
        RequestClose?.Invoke(this, new Minecraft.Classes.AuthServer(AccountType.Yggdrasil, ServerName!)
        {
            ServerUrl = ServerUrl!
        });
    }

    private void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }
}
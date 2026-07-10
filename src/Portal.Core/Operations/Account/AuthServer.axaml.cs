using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft.Account;
using TioUi.Common.Interfaces;

namespace Portal.Core.Operations.Account;

public partial class AuthServer : UserControl
{
    public AuthServer()
    {
        InitializeComponent();
    }
}

public partial class AuthServerViewModel : ObservableObject, IDialogContext, INotifyDataErrorInfo
{
    [ObservableProperty]
    public partial string? ServerName { get; set; }

    [ObservableProperty]
    public partial string? ServerUrl { get; set; }

    public ICommand NextCommand { get; }
    public ICommand CancelCommand { get; }

    private readonly Dictionary<string, List<string>> _errors = new();
    private readonly Core.Minecraft.Account.AuthServer[] _existingServers;

    public AuthServerViewModel(Core.Minecraft.Account.AuthServer[] existingServers)
    {
        _existingServers = existingServers;
        NextCommand = new RelayCommand(Next, CanNext);
        CancelCommand = new RelayCommand(Cancel);
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

        if (_errors.ContainsKey(propertyName))
        {
            _errors.Remove(propertyName);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            _errors[propertyName] = new List<string> { "服务器名称不能为空" };
        }

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }

    private void ValidateServerUrl(string? value)
    {
        var propertyName = nameof(ServerUrl);

        if (_errors.ContainsKey(propertyName))
        {
            _errors.Remove(propertyName);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            _errors[propertyName] = new List<string> { "服务器 URL 不能为空" };
        }
        else if (!IsValidUrl(value))
        {
            _errors[propertyName] = new List<string> { "URL 地址格式不正确" };
        }
        else if (IsUrlExists(value))
        {
            _errors[propertyName] = new List<string> { "该验证服务器已存在" };
        }

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }

    private bool IsValidUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        return Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
            && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }

    private bool IsUrlExists(string url)
    {
        return _existingServers.Any(server =>
            server.AuthType == AccountType.Yggdrasil &&
            !string.IsNullOrEmpty(server.ServerUrl) &&
            string.Equals(server.ServerUrl, url, StringComparison.OrdinalIgnoreCase));
    }

    private bool CanNext()
    {
        return !HasErrors && !string.IsNullOrWhiteSpace(ServerName) && !string.IsNullOrWhiteSpace(ServerUrl);
    }

    private void Next()
    {
        RequestClose?.Invoke(this, new Core.Minecraft.Account.AuthServer(AccountType.Yggdrasil, ServerName!)
        {
            ServerUrl = ServerUrl!
        });
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

    public bool HasErrors => _errors.Count > 0;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName) || !_errors.ContainsKey(propertyName))
        {
            return Enumerable.Empty<string>();
        }
        return _errors[propertyName];
    }
}
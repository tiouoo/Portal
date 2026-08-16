using System.Collections;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Helpers;
using Portal.Core.Minecraft.Classes;
using TioUi.Common.Interfaces;
using Minecraft = Portal.Core.Minecraft;

namespace Portal.Views.Components.Operations.Account;

public partial class EditAuthServer : UserControl
{
    public EditAuthServer()
    {
        InitializeComponent();
    }
}

public partial class EditAuthServerViewModel : ObservableObject, IDialogContext, INotifyDataErrorInfo
{
    private readonly Minecraft.Classes.AuthServer _editingServer;

    private readonly Dictionary<string, List<string>> _errors = new();
    private readonly Minecraft.Classes.AuthServer[] _existingServers;

    public EditAuthServerViewModel(Minecraft.Classes.AuthServer editingServer,
        Minecraft.Classes.AuthServer[] existingServers)
    {
        _editingServer = editingServer;
        _existingServers = existingServers;
        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
        CancelCommand = new RelayCommand(Cancel);
        DeleteCommand = new RelayCommand(Delete);
        ServerName = editingServer.DisplayText;
        ServerUrl = editingServer.ServerUrl;
    }

    [ObservableProperty] public partial string? ServerName { get; set; }

    [ObservableProperty] public partial string? ServerUrl { get; set; }

    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand DeleteCommand { get; }

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
        (ConfirmCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    partial void OnServerUrlChanged(string? value)
    {
        ValidateServerUrl(value);
        (ConfirmCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void ValidateServerName(string? value)
    {
        var propertyName = nameof(ServerName);

        if (_errors.ContainsKey(propertyName)) _errors.Remove(propertyName);

        if (string.IsNullOrWhiteSpace(value)) _errors[propertyName] = new List<string> { "服务器名称不能为空" };

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }

    private void ValidateServerUrl(string? value)
    {
        var propertyName = nameof(ServerUrl);

        if (_errors.ContainsKey(propertyName)) _errors.Remove(propertyName);

        if (string.IsNullOrWhiteSpace(value))
            _errors[propertyName] = new List<string> { "服务器 URL 不能为空" };
        else if (!UrlHelper.IsValidUrl(value))
            _errors[propertyName] = new List<string> { "URL 地址格式不正确" };
        else if (IsUrlExists(value)) _errors[propertyName] = new List<string> { "该验证服务器已存在" };

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }

    private bool IsUrlExists(string url)
    {
        return _existingServers.Any(server =>
            !ReferenceEquals(server, _editingServer) &&
            server.AuthType == AccountType.Yggdrasil &&
            !string.IsNullOrEmpty(server.ServerUrl) &&
            UrlHelper.AreUrlsEqual(server.ServerUrl, url));
    }

    private bool CanConfirm()
    {
        return !HasErrors && !string.IsNullOrWhiteSpace(ServerName) && !string.IsNullOrWhiteSpace(ServerUrl);
    }

    private void Confirm()
    {
        _editingServer.DisplayText = ServerName!;
        _editingServer.ServerUrl = ServerUrl!;
        RequestClose?.Invoke(this, new EditAuthServerResult(_editingServer, false));
    }

    private void Delete()
    {
        RequestClose?.Invoke(this, new EditAuthServerResult(_editingServer, true));
    }

    private void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }
}

public record EditAuthServerResult(Minecraft.Classes.AuthServer Server, bool IsDeleted);
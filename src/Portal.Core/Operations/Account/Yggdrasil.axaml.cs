using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLaunch.Components.Authenticator;
using MinecraftLaunch.Components.Provider;
using Portal.Core.Helpers;
using Portal.Core.Minecraft.Classes;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Extensions;
using TioUi.Common;
using TioUi.Common.Interfaces;
using TioUi.Controls;

namespace Portal.Core.Operations.Account;

public partial class Yggdrasil : UserControl
{
    public Yggdrasil()
    {
        InitializeComponent();
    }
}

public partial class YggdrasilAccountViewModel : ObservableObject, IDialogContext, INotifyDataErrorInfo
{
    private readonly ObservableCollection<Minecraft.Classes.AuthServer> _authServers;
    private readonly string? _hostId;

    [ObservableProperty] public partial string? ServerUrl { get; set; }

    [ObservableProperty] public partial string? Username { get; set; }

    [ObservableProperty] public partial string? Password { get; set; }
    [ObservableProperty] public partial string? ErrMsg { get; set; }
    [ObservableProperty] public partial string? FetchingMsg { get; set; }
    [ObservableProperty] public partial bool IsAuthing { get; set; }
    [ObservableProperty] public partial bool IsError { get; set; }

    public List<Minecraft.Classes.AuthServer> BuiltInServers { get; } = [];

    private Minecraft.Classes.AuthServer? _selectedBuiltInServer;

    public Minecraft.Classes.AuthServer? SelectedBuiltInServer
    {
        get => _selectedBuiltInServer;
        set
        {
            SetProperty(ref _selectedBuiltInServer, value);
            if (value != null && !string.IsNullOrEmpty(value.ServerUrl))
            {
                ServerUrl = value.ServerUrl;
            }
        }
    }

    public ICommand NextCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand AuthServerCommand { get; }

    private readonly Dictionary<string, List<string>> _errors = new();

    public YggdrasilAccountViewModel(ObservableCollection<Minecraft.Classes.AuthServer> authServers,
        string? hostId = null)
    {
        _authServers = authServers;
        _hostId = hostId;
        BuiltInServers.Add(new Minecraft.Classes.AuthServer(AccountType.Yggdrasil, "自定义"));
        BuiltInServers.Add(new Minecraft.Classes.AuthServer(AccountType.Yggdrasil, "LittleSkin")
        {
            ServerUrl = "https://littleskin.cn/api/yggdrasil"
        });

        foreach (var server in authServers)
        {
            bool exists = server.AuthType == AccountType.Yggdrasil
                ? BuiltInServers.Any(item => item.AuthType == AccountType.Yggdrasil &&
                                             !string.IsNullOrEmpty(item.ServerUrl) &&
                                             !string.IsNullOrEmpty(server.ServerUrl) &&
                                             UrlHelper.AreUrlsEqual(item.ServerUrl, server.ServerUrl))
                : BuiltInServers.Contains(server);

            if (!exists)
            {
                BuiltInServers.Add(server);
            }
        }

        if (SelectedBuiltInServer == null)
        {
            SelectedBuiltInServer = BuiltInServers[0];
        }

        NextCommand = new RelayCommand(Next, CanNext);
        RetryCommand = new RelayCommand(Next);
        CancelCommand = new RelayCommand(Cancel);
        AuthServerCommand = new RelayCommand(AddServer);
    }

    partial void OnServerUrlChanged(string? value)
    {
        ValidateServerUrl(value);
        (NextCommand as RelayCommand)?.NotifyCanExecuteChanged();

        if (!string.IsNullOrWhiteSpace(value))
        {
            var matchedServer = BuiltInServers.FirstOrDefault(server =>
                !string.IsNullOrEmpty(server.ServerUrl) &&
                UrlHelper.AreUrlsEqual(server.ServerUrl, value));

            if (matchedServer != null)
            {
                SelectedBuiltInServer = matchedServer;
            }
            else
            {
                SelectedBuiltInServer = BuiltInServers[0];
            }
        }
    }

    partial void OnUsernameChanged(string? value)
    {
        ValidateUsername(value);
        (NextCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private async void AddServer()
    {
        var options = new OverlayDialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            CanResize = false,
            VerticalAnchor = VerticalPosition.Center
        };

        var result = await OverlayDialog.ShowCustomAsync<AuthServer, AuthServerViewModel, Minecraft.Classes.AuthServer>(
            new AuthServerViewModel(_authServers.ToArray()), hostId: _hostId, options: options);

        if (result != null)
        {
            _authServers.Add(result);
            BuiltInServers.Add(result);
            SelectedBuiltInServer = result;
        }
    }

    partial void OnPasswordChanged(string? value)
    {
        ValidatePassword(value);
        (NextCommand as RelayCommand)?.NotifyCanExecuteChanged();
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
            _errors[propertyName] = new List<string> { "API 地址不能为空" };
        }
        else if (!UrlHelper.IsValidUrl(value))
        {
            _errors[propertyName] = new List<string> { "API 地址格式不正确" };
        }

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }

    private void ValidateUsername(string? value)
    {
        var propertyName = nameof(Username);

        if (_errors.ContainsKey(propertyName))
        {
            _errors.Remove(propertyName);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            _errors[propertyName] = new List<string> { "账户不能为空" };
        }

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }

    private void ValidatePassword(string? value)
    {
        var propertyName = nameof(Password);

        if (_errors.ContainsKey(propertyName))
        {
            _errors.Remove(propertyName);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            _errors[propertyName] = new List<string> { "密码不能为空" };
        }

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }

    private bool CanNext()
    {
        return !HasErrors &&
               !IsAuthing &&
               !IsError &&
               !string.IsNullOrWhiteSpace(Username) &&
               !string.IsNullOrWhiteSpace(Username) &&
               !string.IsNullOrWhiteSpace(Password);
    }

    private async void Next()
    {
        IsError = false;
        IsAuthing = true;
        FetchingMsg = "正在验证账户...";
        try
        {
            YggdrasilAuthenticator authenticator = new YggdrasilAuthenticator(ServerUrl, Username, Password);
            var result = await authenticator.AuthenticateAsync();
            if (result == null)
            {
                IsError = true;
                IsAuthing = false;
                ErrMsg = "验证服务器返回成功，但未接收到账户数据。";
            }
            
            Logger.Info("验证成功 \n" + result.AsJson());

            var yggdrasilAccounts = result.ToList();
            if (yggdrasilAccounts.Any())
            {
                FetchingMsg = $"正在获取账户信息，已完成：(0/{yggdrasilAccounts.Count})";
                List<MinecraftAccount> accounts = [];
                var i = 0;
                foreach (var account in yggdrasilAccounts)
                {
                    var base64 = MinecraftAccount.SteveSkin;
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        await using var skinStream = await SkinProvider.GetYggdrasilSkinDataAsync(account, cts.Token);
                        using var ms = new MemoryStream();
                        await skinStream.CopyToAsync(ms, cts.Token);
                        base64 = ms.ToArray().ToBase64();
                    }
                    catch (Exception e)
                    {
                        Logger.Error("获取皮肤失败 " + e.Message);
                    }

                    var minecraftAccount = new MinecraftAccount(AccountType.Yggdrasil)
                    {
                        AccessToken = account.AccessToken,
                        ClientToken = account.ClientToken,
                        CreateAt = DateTime.Now,
                        Uuid = account.Uuid,
                        Name = account.Name,
                        YggdrasilServerUrl = ServerUrl,
                        Skin = base64,
                        ServerNote = 
                            BuiltInServers.FirstOrDefault(x => UrlHelper.AreUrlsEqual(x.ServerUrl, ServerUrl))
                                .DisplayText,
                        MetaData = account.MetaData,
                        Email = Username,
                        Password = Password,
                    };
                    accounts.Add(minecraftAccount);
                    i++;
                    FetchingMsg = $"正在获取账户信息，已完成：({i}/{yggdrasilAccounts.Count})";
                }

                RequestClose?.Invoke(this, accounts.ToArray());
            }
            else
            {
                IsError = true;
                IsAuthing = false;
                ErrMsg = "验证服务器返回成功，但未接收到账户数据。";
            }
        }
        catch (Exception e)
        {
            IsError = true;
            IsAuthing = false;
            ErrMsg = e.Message;
        }
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
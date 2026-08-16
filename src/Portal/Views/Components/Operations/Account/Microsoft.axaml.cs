using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLaunch.Components.Authenticator;
using MinecraftLaunch.Components.Provider;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Services;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Interfaces;

namespace Portal.Views.Components.Operations.Account;

public partial class Microsoft : UserControl
{
    private bool _fl = true;

    public Microsoft()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (!_fl) return;
            _fl = false;
            _ = (DataContext as MicrosoftAccountViewModel).Auth();
        };
    }

    private void CopyUrl(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this).Clipboard;
        clipboard?.SetTextAsync((DataContext as MicrosoftAccountViewModel).Url);
        TopLevel.GetTopLevel(this)!.Notice("已复制到剪切板", NotificationType.Success);
    }

    private void CopyCode(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this).Clipboard;
        clipboard?.SetTextAsync((DataContext as MicrosoftAccountViewModel)._code);
        TopLevel.GetTopLevel(this)!.Notice("已复制到剪切板", NotificationType.Success);
    }

    private void OpenBrowser(object? sender, RoutedEventArgs e)
    {
        var launcher = TopLevel.GetTopLevel(this).Launcher;
        launcher.LaunchUriAsync(new Uri((DataContext as MicrosoftAccountViewModel).Url));
        TopLevel.GetTopLevel(this)!.Notice("已打开浏览器", NotificationType.Success);
    }
}

public partial class MicrosoftAccountViewModel : ObservableObject, IDialogContext
{
    public string _code;
    [ObservableProperty] public partial bool IsReady { get; set; }
    [ObservableProperty] public partial bool IsError { get; set; }
    [ObservableProperty] public partial bool IsAuthing { get; set; }
    [ObservableProperty] public partial string Msg { get; set; } = "打开微软账户的验证页面，并输入下方的验证码。";
    [ObservableProperty] public partial string Error { get; set; }
    [ObservableProperty] public partial string Code { get; set; }
    [ObservableProperty] public partial string Url { get; set; }
    public RelayCommand Cancel => new(Close);
    public RelayCommand Retry => new(() => { RequestClose.Invoke(this, "retry"); });


    public void Close()
    {
        RequestClose.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;

    public async Task Auth()
    {
        try
        {
            var authenticator = new MicrosoftAuthenticator(CredentialsService.MicrosoftClientId);
            var oAuth2Token = await authenticator.DeviceFlowAuthAsync(deviceCode =>
            {
                IsReady = true;
                Console.WriteLine($"请访问以登录: {deviceCode.VerificationUrl}");
                Console.WriteLine($"输入一次性代码: {deviceCode.UserCode}");
                Code = deviceCode.UserCode;
                _code = deviceCode.UserCode;
                Url = deviceCode.VerificationUrl;
            });
            Msg = "登录完成，正在获取账户信息。";
            IsAuthing = true;
            var account = await authenticator.AuthenticateAsync(oAuth2Token);

            var skin = MinecraftAccount.SteveSkin;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await using var skinStream = await SkinProvider.GetMicrosoftSkinDataAsync(account, cts.Token);
                using var ms = new MemoryStream();
                await skinStream.CopyToAsync(ms, cts.Token);
                skin = ms.ToArray().ToBase64();
            }
            catch
            {
            }

            RequestClose.Invoke(this, new MinecraftAccount(AccountType.Microsoft)
            {
                LastRefreshTime = DateTime.Now,
                RefreshToken = account.RefreshToken,
                AccessToken = account.AccessToken,
                Uuid = account.Uuid,
                Name = account.Name,
                Skin = skin
            });
        }
        catch (Exception e)
        {
            IsError = true;
            Error = e.ToString();
        }
    }
}
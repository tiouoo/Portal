using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iridium.Providers.Minecraft;
using Iridium.Services.Authentication;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Services;
using Portal.Localization;
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
        TopLevel.GetTopLevel(this)!.Notice(CommonLanguageManager.Instance.account_copiedToClipboardVariant.CurrentValue(),
            NotificationType.Success);
    }

    private void CopyCode(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this).Clipboard;
        clipboard?.SetTextAsync((DataContext as MicrosoftAccountViewModel)._code);
        TopLevel.GetTopLevel(this)!.Notice(CommonLanguageManager.Instance.account_copiedToClipboardVariant.CurrentValue(),
            NotificationType.Success);
    }

    private void OpenBrowser(object? sender, RoutedEventArgs e)
    {
        var launcher = TopLevel.GetTopLevel(this).Launcher;
        launcher.LaunchUriAsync(new Uri((DataContext as MicrosoftAccountViewModel).Url));
        TopLevel.GetTopLevel(this)!.Notice(CommonLanguageManager.Instance.account_browserOpened.CurrentValue(),
            NotificationType.Success);
    }
}

public partial class MicrosoftAccountViewModel : ObservableObject, IDialogContext
{
    public string _code;
    [ObservableProperty] public partial bool IsReady { get; set; }
    [ObservableProperty] public partial bool IsError { get; set; }
    [ObservableProperty] public partial bool IsAuthing { get; set; }
    [ObservableProperty] public partial string Msg { get; set; } =
        CommonLanguageManager.Instance.account_openVerification.CurrentValue();
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
                Console.WriteLine(string.Format(LogLanguageManager.Instance.microsoft_visitToLogin.CurrentValue(),
                    deviceCode.VerificationUrl));
                Console.WriteLine(string.Format(LogLanguageManager.Instance.microsoft_enterOneTimeCode.CurrentValue(),
                    deviceCode.UserCode));
                Code = deviceCode.UserCode;
                _code = deviceCode.UserCode;
                Url = deviceCode.VerificationUrl;
            });
            Msg = CommonLanguageManager.Instance.account_loginComplete.CurrentValue();
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
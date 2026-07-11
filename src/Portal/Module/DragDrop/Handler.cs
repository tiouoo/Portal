using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Portal.Const;
using Portal.Core.Minecraft.Account;
using Portal.Core.Operations.Account;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Controls;
using AuthServer = Portal.Core.Operations.Account.AuthServer;

namespace Portal.Module.DragDrop;

public class Handler
{
    public static async void Handle(DragEventArgs e, TioTabWindowBase window)
    {
        var data = e.DataTransfer;
        if (data.Contains(DataFormat.Text))
        {
            var text = data.TryGetText();
            if (TryParseAuthlibUrl(text, out var apiUrl, out var domain))
            {
                e.Handled = true;
                if (!string.IsNullOrEmpty(apiUrl) && !string.IsNullOrEmpty(domain))
                {
                    await HandleAuthServerUrlAsync(apiUrl, domain, window);
                }
            }
        }
    }

    private static async Task HandleAuthServerUrlAsync(string url, string domain, TioTabWindowBase window)
    {
        var hostId = window.HostId;
        var options = new OverlayDialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            CanResize = false,
            VerticalOffset = 110,
            VerticalAnchor = VerticalPosition.Top
        };

        var result = await OverlayDialog
            .ShowCustomAsync<AuthServerDetected, AuthServerDetectedViewModel, AuthServerDetectedAction>(
                new AuthServerDetectedViewModel(url), hostId: hostId, options: options);

        switch (result)
        {
            case AuthServerDetectedAction.AddServer:
                await AddAuthServerAsync(url, domain, hostId, options, window);
                break;
            case AuthServerDetectedAction.Login:
                await LoginAccountAsync(url, hostId, options, window);
                break;
        }
    }

    private static async Task AddAuthServerAsync(string url, string domain, string? hostId,
        OverlayDialogOptions options, TioTabWindowBase window)
    {
        var existingServers = Data.ConfigEntry.AuthServers.ToArray();

        var vm = new AuthServerViewModel(existingServers)
        {
            ServerName = domain,
            ServerUrl = url
        };

        var result = await OverlayDialog
            .ShowCustomAsync<AuthServer, AuthServerViewModel, global::Portal.Core.Minecraft.Account.AuthServer>(
                vm, hostId: hostId, options: options);

        if (result != null)
        {
            Data.ConfigEntry.AuthServers.Add(result);
            App.Method.SaveConfig();
            window.TryGetToast()?.Show("验证服务器已添加", NotificationType.Success);
        }
    }

    private static async Task LoginAccountAsync(string url, string? hostId, OverlayDialogOptions options,
        TioTabWindowBase window)
    {
        var result = await OverlayDialog.ShowCustomAsync<Yggdrasil, YggdrasilAccountViewModel, MinecraftAccount[]>(
            new YggdrasilAccountViewModel(Data.ConfigEntry.AuthServers, hostId) { ServerUrl = url }, hostId: hostId,
            options: options);

        if (result == null || result.Length == 0) return;

        foreach (var account in result)
        {
            if (account is null) continue;
            Data.ConfigEntry.MinecraftAccounts.Add(account);
        }

        Data.ConfigEntry.UsingMinecraftMinecraftAccount = result.LastOrDefault();
    }

    private static bool TryParseAuthlibUrl(string? input, out string? apiUrl, out string? domain)
    {
        apiUrl = null;
        domain = null;

        if (string.IsNullOrWhiteSpace(input)) return false;

        var trimmed = input.Trim();
        const string PREFIX = "authlib-injector:yggdrasil-server:";
        if (!trimmed.StartsWith(PREFIX, StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            var encodedPart = trimmed.Substring(PREFIX.Length);
            var decoded = System.Net.WebUtility.UrlDecode(encodedPart);

            if (Uri.TryCreate(decoded, UriKind.Absolute, out var uri))
            {
                if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                {
                    apiUrl = decoded;
                    domain = uri.Host;
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
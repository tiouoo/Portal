using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using MinecraftLaunch.Components.Authenticator;
using Portal.Core.Minecraft.Classes;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Core.Operations.Account;

public class AddAccount
{
    public static async Task<MinecraftAccount[]?> Main(string hostId,
        ObservableCollection<Minecraft.Classes.AuthServer> authServers)
    {
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
            .ShowCustomAsync<SelectAccountType, SelectAccountTypeViewModel, SelectAccountTypeResult>(
                new SelectAccountTypeViewModel(), hostId: hostId, options: options);

        if (result?.Action != SelectAccountTypeAction.Select || result.SelectedServer == null)
        {
            return null;
        }

        var accounts = await HandleAccountType(result.SelectedServer, authServers, hostId);

        if (accounts == null || accounts.Length == 0 || accounts.All(a => a == null))
        {
            return null;
        }

        var validAccounts = accounts.Where(a => a != null).ToArray();
        var viewResult = await OverlayDialog.ShowCustomAsync<ViewResult, ViewResultViewModel, object>(
            new ViewResultViewModel(new ObservableCollection<MinecraftAccount>(validAccounts)),
            hostId: hostId, options: options);

        if (viewResult is ObservableCollection<MinecraftAccount> resultAccounts)
        {
            return resultAccounts.ToArray();
        }

        return null;
    }

    private static async Task<MinecraftAccount[]?> HandleAccountType(Minecraft.Classes.AuthServer authServer,
        ObservableCollection<Minecraft.Classes.AuthServer> authServers, string? hostId)
    {
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

        return authServer.AuthType switch
        {
            AccountType.Offline => await Offline(hostId, options),
            AccountType.Microsoft => await Microsoft(hostId, options),
            AccountType.Yggdrasil => await Yggdrasil(hostId, options, authServers),
            _ => null
        };
    }

    public static async Task<MinecraftAccount[]?> Offline(string? hostId, OverlayDialogOptions options)
    {
        var result = await OverlayDialog.ShowCustomAsync<Offline, OfflineAccountViewModel, MinecraftAccount>(
            new OfflineAccountViewModel(), hostId: hostId, options: options);

        return [result];
    }

    public static async Task<MinecraftAccount[]?> Microsoft(string? hostId, OverlayDialogOptions options)
    {
        var result = await OverlayDialog.ShowCustomAsync<Account.Microsoft, MicrosoftAccountViewModel, object>(
            new MicrosoftAccountViewModel(), hostId: hostId, options: options);

        if (result is "retry")
        {
            return await Microsoft(hostId, options);
        }

        return [result as MinecraftAccount];
    }

    public static async Task<MinecraftAccount[]?> Yggdrasil(string? hostId, OverlayDialogOptions options,
        ObservableCollection<Minecraft.Classes.AuthServer> authServers)
    {
        var result = await OverlayDialog.ShowCustomAsync<Yggdrasil, YggdrasilAccountViewModel, MinecraftAccount[]>(
            new YggdrasilAccountViewModel(authServers, hostId), hostId: hostId, options: options);

        if (result == null)
        {
            return null;
        }

        foreach (var account in result)
        {
            var host = Uri.TryCreate(account.YggdrasilServerUrl, UriKind.Absolute, out var uriResult) ? uriResult.Host : "";
            account.AccountNote = host;
            account.CreateAt = DateTime.Now;
            account.LastRefreshTime = DateTime.Now;
        }

        return result;
    }
}
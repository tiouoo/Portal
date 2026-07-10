using System.Collections.ObjectModel;
using Avalonia;
using Portal.Core.Minecraft.Account;
using TioUi.Common;
using TioUi.Controls;

namespace Portal.Core.Operations.Account;

public class AddAccount
{
    public static async Task<MinecraftAccount?> Main(object sender, ObservableCollection<Minecraft.Account.AuthServer> authServers)
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

        var result = await OverlayDialog.ShowCustomAsync<SelectAccountType, SelectAccountTypeViewModel, SelectAccountTypeResult>(
            new SelectAccountTypeViewModel(authServers), hostId: null, options: options);

        if (result?.Action != SelectAccountTypeAction.Select || result.SelectedServer == null)
        {
            return null;
        }

        return await HandleAccountType(result.SelectedServer);
    }

    private static async Task<MinecraftAccount?> HandleAccountType(Minecraft.Account.AuthServer authServer)
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
            AccountType.Offline => await Offline(options),
            AccountType.Microsoft => await Microsoft(options),
            AccountType.Yggdrasil => await Yggdrasil(options, authServer),
            _ => null
        };
    }

    public static async Task<MinecraftAccount?> Offline(OverlayDialogOptions options)
    {
        var result = await OverlayDialog.ShowCustomAsync<Offline, OfflineAccountViewModel, MinecraftAccount>(
            new OfflineAccountViewModel(), hostId: null, options: options);

        return result;
    }

    public static async Task<MinecraftAccount?> Microsoft(OverlayDialogOptions options)
    {
        var result = await OverlayDialog.ShowCustomAsync<Account.Microsoft, MicrosoftAccountViewModel, object>(
            new MicrosoftAccountViewModel(), hostId: null, options: options);

        if (result is "retry")
        {
            return await Microsoft(options);
        }

        return result as MinecraftAccount;
    }

    public static async Task<MinecraftAccount?> Yggdrasil(OverlayDialogOptions options,
        Minecraft.Account.AuthServer authServer)
    {
        return null;
    }
}
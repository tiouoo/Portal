using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Markup.Xaml.Templates;
using Portal.Core.Minecraft.Account;
using Portal.Core.Operations.Account;
using TioUi.Common;
using TioUi.Controls;

namespace Portal.Core.Operations;

public class AddAccount
{
    public static async Task<MinecraftAccount?> Main(object sender)
    {
        var options = new OverlayDialogOptions()
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.OKCancel,
            Title = "添加新账户",
            VerticalOffset = 100,

            CanLightDismiss = false,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            CanResize = false,
            OverrideOkButtonText = "下一步"
        };
        var items = new List<AuthServer>
        {
            new(AccountType.Offline, "离线模式"),
            new(AccountType.Microsoft, "微软账户"),
            new(AccountType.Yggdrasil, "外置登录"),
            new(AccountType.Yggdrasil, "LittleSkin") { ServerUrl = "https://littleskin.cn/api/yggdrasil" }
        };
        var type = new ComboBox()
        {
            ItemsSource = items,
            SelectedIndex = 0,
            Width = 320,
            Margin = new Thickness(0, 15),
            ItemTemplate = new FuncDataTemplate<AuthServer>((data, _) =>
                new TextBlock { Text = data.DisplayText })
        };
        var result = await OverlayDialog.ShowStandardAsync(type, vm: null, hostId: null, options: options);
        if (result != DialogResult.OK)
        {
            return null;
        }

        if (type.SelectedItem is AuthServer authServer)
        {
            switch (authServer.AuthType)
            {
                case AccountType.Offline:
                    return await Offline();
                case AccountType.Microsoft:
                    break;
                case AccountType.Yggdrasil:
                    break;
            }
        }

        return null;
    }

    public static async Task<MinecraftAccount?> Offline()
    {
        var options = new OverlayDialogOptions()
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            Title = "创建离线账户",
            CanLightDismiss = false,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            CanResize = false,
            OverrideOkButtonText = "下一步"
        };

        var result = await OverlayDialog.ShowCustomAsync<Offline, OfflineAccountViewModel, MinecraftAccount>(
            new OfflineAccountViewModel(), hostId: null, options: options);

        return result;
    }
}
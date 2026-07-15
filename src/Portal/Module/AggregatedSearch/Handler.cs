using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Portal.Classes.Entries;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Operations.Account;
using Portal.Views.Pages;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;
using AuthServer = Portal.Core.Minecraft.Classes.AuthServer;
using TopLevel = Avalonia.Controls.TopLevel;


namespace Portal.Module.AggregatedSearch;

public class Handler
{
    public static void HandleAsync(AggregatedSearchEntry entry, TopLevel sender)
    {
        if (entry.Type == AggregatedSearchEntryType.Page)
        {
            HandlePage(entry, sender);
        }
        else if (entry.Type == AggregatedSearchEntryType.Instance)
        {
            HandleInstance(entry, sender);
        }
        else if (entry.Type == AggregatedSearchEntryType.Account)
        {
            var minecraftAccount = entry.Data as MinecraftAccount;
            Data.ConfigEntry.UsingMinecraftMinecraftAccount = minecraftAccount;
            NotificationGateway.Notice(sender, $"已切换到 {minecraftAccount.Name}", NotificationType.Success);
        }
        else if (entry.Type == AggregatedSearchEntryType.AuthServer)
        {
            _ = EditAuthServer(entry, sender);
        }
    }

    private static void HandlePage(AggregatedSearchEntry entry, TopLevel sender)
    {
        var pageType = entry.Data as Type;
        if (pageType == null) return;

        var tabWindow = sender as TioTabWindowBase;
        if (tabWindow == null) return;

        ITioTabPage? page = null;

        if (pageType == typeof(NewTabPage))
        {
            page = new NewTabPage();
        }
        else if (pageType == typeof(InstancesPage))
        {
            page = new InstancesPage();
        }
        else if (pageType == typeof(NewsPage))
        {
            page = new NewsPage();
        }
        else
        {
            var settingPage = new SettingPage();
            settingPage.NavigateTo(pageType);
            page = settingPage;
        }

        if (page != null)
        {
            var tab = new TabEntry(tabWindow, page);
            tabWindow.CreateTab(tab);
            tabWindow.SelectTab(tab);
        }
    }

    private static void HandleInstance(AggregatedSearchEntry entry, TopLevel sender)
    {
        var instance = entry.Data as MinecraftInstance;
        if (instance == null) return;
    }

    private static async Task EditAuthServer(AggregatedSearchEntry entry, TopLevel sender)
    {
        var authServer = entry.Data as AuthServer;
        if (authServer == null) return;

        var hostId = sender.TryGetHostId();

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

        var result = await OverlayDialog
            .ShowCustomAsync<EditAuthServer, EditAuthServerViewModel, EditAuthServerResult>(
                new EditAuthServerViewModel(authServer, Data.ConfigEntry.AuthServers.ToArray()),
                hostId: hostId, options: options);

        if (result != null)
        {
            if (result.IsDeleted)
            {
                Data.ConfigEntry.AuthServers.Remove(result.Server);
                NotificationGateway.Notice(sender, $"已删除验证服务器：{result.Server.DisplayText}", NotificationType.Success);
            }
            else
            {
                App.Method.SaveConfig();
                NotificationGateway.Notice(sender, "验证服务器已更新", NotificationType.Success);
            }
        }
    }
}
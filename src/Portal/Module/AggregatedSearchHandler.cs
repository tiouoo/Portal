using Avalonia.Controls.Notifications;
using Portal.Core.Classes.Entries;
using Portal.Core.Const;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Module.Initialize;
using Portal.Core.Services;
using Portal.Views.Operations.Account;
using Portal.Views.Pages;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Gateway;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;
using AuthServer = Portal.Core.Minecraft.Classes.AuthServer;
using EditAuthServerViewModel = Portal.Views.Operations.Account.EditAuthServerViewModel;
using TopLevel = Avalonia.Controls.TopLevel;


namespace Portal.Module;

public class AggregatedSearchHandler
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
        else if (entry.Type == AggregatedSearchEntryType.RecentPlay)
        {
            HandleRecentPlay(entry, sender);
        }
        else if (entry.Type == AggregatedSearchEntryType.Account)
        {
            var minecraftAccount = entry.Data as MinecraftAccount;
            Data.ConfigEntry.UsingMinecraftMinecraftAccount = minecraftAccount;
            sender.Notice($"已切换到 {minecraftAccount.Name}", NotificationType.Success);
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

        ITioTabPage page;

        if (Activator.CreateInstance(pageType) is ITioTabPage tabPage)
        {
            page = tabPage;
        }
        else
        {
            var settingPage = new SettingPage();
            settingPage.NavigateTo(pageType);
            page = settingPage;
        }

        var tab = new TabEntry(tabWindow, page);
        tabWindow.CreateTab(tab);
        tabWindow.SelectTab(tab);
    }

    private static void HandleInstance(AggregatedSearchEntry entry, TopLevel sender)
    {
        var instance = entry.Data as MinecraftInstance;
        if (instance == null) return;
        InstanceDetailPage.Open(instance, sender);
    }

    private static void HandleRecentPlay(AggregatedSearchEntry entry, TopLevel sender)
    {
        if (entry.Data is not RecentPlayTarget target) return;

        _ = MinecraftLaunchService.LaunchAsync(target.Instance, sender,
            MinecraftLaunchOptionsFactory.Create(target.Instance,
                logSession => MinecraftLogPage.Open(logSession, sender)), target);
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
            VerticalAnchor = VerticalPosition.Top,
            VerticalOffset = 110
        };

        var result = await OverlayDialog
            .ShowCustomAsync<EditAuthServer, EditAuthServerViewModel, EditAuthServerResult>(
                new EditAuthServerViewModel(authServer, Data.ConfigEntry.AuthServers.ToArray()),
                hostId, options);

        if (result != null)
        {
            if (result.IsDeleted)
            {
                Data.ConfigEntry.AuthServers.Remove(result.Server);
                sender.Notice($"已删除验证服务器：{result.Server.DisplayText}", NotificationType.Success);
            }
            else
            {
                ConfigSaver.SaveConfig();
                sender.Notice("验证服务器已更新", NotificationType.Success);
            }
        }
    }
}
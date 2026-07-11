using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Portal.Const;
using Portal.Core.Minecraft.Account;
using Portal.Core.Operations;
using Portal.Core.Operations.Account;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Tab.Extensions;
using TioUi.Common.Classes;
using TioUi.Common.Extensions;

namespace Portal.Views.Components;

public partial class TitleBarComponent : Grid
{
    public TitleBarComponent()
    {
        InitializeComponent();
        DataContext = this;
    }

    public Data Data { get; set; } = Data.Instance;

    private void ThemeMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not string themeName) return;

        Data.ConfigEntry.Theme = themeName switch
        {
            "System" => TioUi.Shared.Theme.System,
            "Light" => TioUi.Shared.Theme.Light,
            "Dark" => TioUi.Shared.Theme.Dark,
            "Mirage" => TioUi.Shared.Theme.Mirage,
            _ => Data.ConfigEntry.Theme
        };
    }

    private async void AccountButton_Click(object? sender, RoutedEventArgs e)
    {
        if (Data.ConfigEntry.MinecraftAccounts.Count != 0)
        {
            AccountFlyout.Flyout.ShowAt(AccountFlyoutPoint);
            return;
        }

        var result = await AddAccount.Main(((Control)sender!).TryGetHostId()!, Data.ConfigEntry.AuthServers);
        if (result == null || result.Length == 0) return;
        foreach (var minecraftAccount in result)
        {
            if (minecraftAccount is null) continue;
            Data.ConfigEntry.MinecraftAccounts.Add(minecraftAccount);
        }

        if (result.Length == 1 && result[0] == null) return;
        Data.ConfigEntry.UsingMinecraftMinecraftAccount = result.LastOrDefault();
    }

    private async void AddAcountButton_OnClick(object? sender, RoutedEventArgs e)
    {
        AccountFlyout.Flyout.Hide();
        var tryGetHostId = ((Control)Root!).TryGetHostId()!;
        var result = await AddAccount.Main(tryGetHostId, Data.ConfigEntry.AuthServers);
        if (result == null || result.Length == 0) return;
        foreach (var minecraftAccount in result)
        {
            if (minecraftAccount is null) continue;
            Data.ConfigEntry.MinecraftAccounts.Add(minecraftAccount);
        }

        if (result.Length == 1 && result[0] == null) return;
        Data.ConfigEntry.UsingMinecraftMinecraftAccount = result.LastOrDefault();
    }

    public void DeleteAccount(object parameter)
    {
        if (parameter is not MinecraftAccount account) return;
        if (Data.ConfigEntry.UsingMinecraftMinecraftAccount == account)
        {
            Data.ConfigEntry.MinecraftAccounts.Remove(account);
            Data.ConfigEntry.UsingMinecraftMinecraftAccount = Data.ConfigEntry.MinecraftAccounts.FirstOrDefault();
        }
        else
        {
            Data.ConfigEntry.MinecraftAccounts.Remove(account);
        }
        
        Root.TryGetToast()?.Show(new NotificationOptions()
        {
            Content = $"已移除账户：{account.Name} ({account.DisplayAccountNote})",
            Type = NotificationType.Success,
            Expiration = TimeSpan.FromSeconds(3),
            OperateButtons = [
                new OperateButtonEntry("撤销", _ =>
                {
                    Data.ConfigEntry.MinecraftAccounts.Add(account);
                    Data.ConfigEntry.UsingMinecraftMinecraftAccount = account;
                }, true),
            ]
        });

        if (Data.ConfigEntry.MinecraftAccounts.Count == 0)
            AccountFlyout.Flyout.Hide();
    }
}
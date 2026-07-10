using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Portal.Const;
using Portal.Core.Minecraft.Account;
using Portal.Core.Operations;

namespace Portal.Views.Components;

public partial class TitleBarComponent : StackPanel
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
            AccountFlyout.Flyout.ShowAt(AccountButton);
            return;
        }

        var result = await AddAccount.Main(sender!);
        if (result == null) return;
        Data.ConfigEntry.MinecraftAccounts.Add(result);
        Data.ConfigEntry.UsingMinecraftMinecraftAccount = result;
    }

    private async void AddAcountButton_OnClick(object? sender, RoutedEventArgs e)
    {
        AccountFlyout.Flyout.Hide();
        var result = await AddAccount.Main(sender!);
        if (result == null) return;
        Data.ConfigEntry.MinecraftAccounts.Add(result);
        Data.ConfigEntry.UsingMinecraftMinecraftAccount = result;
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

        if (Data.ConfigEntry.MinecraftAccounts.Count == 0)
            AccountFlyout.Flyout.Hide();
    }
}
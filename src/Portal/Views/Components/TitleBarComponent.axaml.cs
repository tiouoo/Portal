using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using Portal.Classes.Entries;
using Portal.Const;
using Portal.Core.Minecraft.Account;
using Portal.Core.Operations;
using Portal.Core.Operations.Account;
using Portal.Views.Pages;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Views.Components;

public partial class TitleBarComponent : StackPanel
{
    public TitleBarComponent()
    {
        InitializeComponent();
        DataContext = this;
        DeleteAccountCommand = new RelayCommand<MinecraftAccount>(DeleteAccount);
    }
    
    public Data Data { get; set; } = Data.Instance;
    public RelayCommand<MinecraftAccount> DeleteAccountCommand { get; }

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

    private void SettingMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        var window = global::Avalonia.Controls.TopLevel.GetTopLevel(Root) as TioTabWindowBase;
        var tabEntry = new TabEntry(window!, new SettingPage());
        window?.CreateTab(tabEntry);
        window?.SelectTab(tabEntry);
    }

    private void AcrylicToggle_OnClick(object? sender, RoutedEventArgs e)
    {
        Data.ConfigEntry.AcrylicEnabled = !Data.ConfigEntry.AcrylicEnabled;
    }

    private async void AccountButton_Click(object? sender, RoutedEventArgs e)
    {
        if (Data.ConfigEntry.MinecraftAccounts.Count != 0)
        {
            AccountFlyout.Flyout.ShowAt(AccountButton);
            return;
        }

        var result = await AddAccount.Main(null, Data.ConfigEntry.AuthServers);
        if (result == null) return;
        foreach (var acc in result)
            Data.ConfigEntry.MinecraftAccounts.Add(acc);
        Data.ConfigEntry.UsingMinecraftMinecraftAccount = Data.ConfigEntry.MinecraftAccounts.LastOrDefault();
    }

    private async void AddAcountButton_OnClick(object? sender, RoutedEventArgs e)
    {
        AccountFlyout.Flyout.Hide();
        var result = await AddAccount.Main(null, Data.ConfigEntry.AuthServers);
        if (result == null) return;
        foreach (var acc in result)
            Data.ConfigEntry.MinecraftAccounts.Add(acc);
        Data.ConfigEntry.UsingMinecraftMinecraftAccount = Data.ConfigEntry.MinecraftAccounts.LastOrDefault();
    }

    public void DeleteAccount(MinecraftAccount account)
    {
        if (Data.ConfigEntry.UsingMinecraftMinecraftAccount == account)
        {
            Data.ConfigEntry.MinecraftAccounts.Remove(account);
            Data.ConfigEntry.UsingMinecraftMinecraftAccount = Data.ConfigEntry.MinecraftAccounts.FirstOrDefault();
        }
        else
        {
            Data.ConfigEntry.MinecraftAccounts.Remove(account);
        }
    }
}
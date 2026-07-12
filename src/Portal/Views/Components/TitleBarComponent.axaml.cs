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
        if (sender is not Button btn || btn.Tag is not string themeName) return;

        Data.ConfigEntry.Theme = themeName switch
        {
            "System" => TioUi.Shared.Theme.System,
            "Light" => TioUi.Shared.Theme.Light,
            "Dark" => TioUi.Shared.Theme.Dark,
            "Mirage" => TioUi.Shared.Theme.Mirage,
            _ => Data.ConfigEntry.Theme
        };
    }

    private void BackgroundMode_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string modeName) return;

        Data.ConfigEntry.BackgroundMode = modeName switch
        {
            "Default" => BackgroundMode.Default,
            "Image" => BackgroundMode.Image,
            "SolidColor" => BackgroundMode.SolidColor,
            "Acrylic" => BackgroundMode.Acrylic,
            _ => Data.ConfigEntry.BackgroundMode
        };
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
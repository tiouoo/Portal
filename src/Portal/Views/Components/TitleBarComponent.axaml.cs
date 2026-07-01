using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Portal.Const;
using Portal.Core.Operations;

namespace Portal.Views.Components;

public partial class TitleBarComponent : StackPanel
{
    public TitleBarComponent()
    {
        InitializeComponent();
    }

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
        Data.ConfigEntry.UsingMinecraftAccount = result;
    }
}
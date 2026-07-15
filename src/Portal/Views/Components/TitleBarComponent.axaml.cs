using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Operations;
using Portal.Core.Operations.Account;
using Portal.Views.Pages;
using Portal.Views.Pages.SettingPages;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Common.Classes;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Components;

public partial class TitleBarComponent : Grid
{
    public TitleBarComponent()
    {
        InitializeComponent();
        DataContext = this;
    }

    public static readonly StyledProperty<string?> DropMsgProperty =
        AvaloniaProperty.Register<TitleBarComponent, string?>(nameof(DropMsg));

    public string? DropMsg
    {
        get => GetValue(DropMsgProperty);
        set => SetValue(DropMsgProperty, value);
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

        NotificationGateway.Notice(Root.GetTopLevel(), new NotificationOptions()
        {
            Content = $"已移除账户：{account.Name} ({account.DisplayAccountNote})",
            Type = NotificationType.Success,
            Expiration = TimeSpan.FromSeconds(3),
            OperateButtons =
            [
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

    private void OpenSearch(object? sender, RoutedEventArgs e)
    {
        var options = new DialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            StyleClass = "undrag",
            CanResize = true,
            StartupLocation = WindowStartupLocation.CenterOwner,
            DialogWindowMinWidth = 770,
            DialogWindowMinHeight = 471,
            DialogWindowWidth = 770,
            DialogWindowHeight = 471,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        _ = Dialog.ShowCustomAsync<AggregatedSearchDialog, AggregatedSearchDialogViewModel, object>(
            new AggregatedSearchDialogViewModel((Root.GetTopLevel() as TioTabWindowBase)!), options: options,
            owner: (Root.GetTopLevel() as TioTabWindowBase)!);
    }

    private void SettingMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        var tioTabWindowBase = Root.GetTopLevel() as TioTabWindowBase;
        var tabEntry = new TabEntry(tioTabWindowBase!, new SettingPage());
        tioTabWindowBase.CreateTab(tabEntry);
        tioTabWindowBase.SelectTab(tabEntry);
    }

    private void GoToAbout(object? sender, RoutedEventArgs e)
    {
        var tioTabWindowBase = Root.GetTopLevel() as TioTabWindowBase;
        var tioTabPage = new SettingPage();
        tioTabPage.SettingPageViewModel.NavigateType(typeof(About));
        tioTabPage.NavMenu.SelectedItem = tioTabPage.AboutItem;
        var tabEntry = new TabEntry(tioTabWindowBase!, tioTabPage);
        tioTabWindowBase.CreateTab(tabEntry);
        tioTabWindowBase.SelectTab(tabEntry);
    }

    private async void ChangeSkin_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not MinecraftAccount account) return;

        AccountFlyout.Flyout.Hide();
        var hostId = ((Control)Root!).TryGetHostId();
        var result = await ChangeSkinDialog.Show(hostId, null);
        // TODO: handle result (skin path)
    }
    
    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        AccountFlyout.Flyout.Hide();
        var tioTabWindowBase = Root.GetTopLevel() as TioTabWindowBase;
        var tioTabPage = new SettingPage();
        tioTabPage.NavigateTo(typeof(Account));
        var tabEntry = new TabEntry(tioTabWindowBase!, tioTabPage);
        tioTabWindowBase.CreateTab(tabEntry);
        tioTabWindowBase.SelectTab(tabEntry);
    }
}
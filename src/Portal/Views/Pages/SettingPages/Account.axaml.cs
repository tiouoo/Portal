using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Operations.Account;
using Portal.Module.AggregatedSearch;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Classes;
using TioUi.Common.Extensions;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("账户与档案", "设置/账户与档案", "Account")]
public partial class Account : DataUserControl
{
    public Account()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void SaveSkin_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: MinecraftAccount account })
            return;

        _ = SaveSkinAsync(account);
    }

    private async Task SaveSkinAsync(MinecraftAccount account)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "保存皮肤",
                SuggestedFileName = $"{account.Name}.png",
                FileTypeChoices =
                [
                    new Avalonia.Platform.Storage.FilePickerFileType("PNG 图片") { Patterns = ["*.png"] }
                ],
            });

        if (file == null) return;

        try
        {
            var skinBytes = Convert.FromBase64String(account.Skin);
            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(skinBytes);
            NotificationGateway.Notice(topLevel, "皮肤已保存", NotificationType.Success);
        }
        catch (Exception ex)
        {
            NotificationGateway.Notice(topLevel, $"保存失败：{ex.Message}", NotificationType.Error);
        }
    }

    private void RefreshInfo_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: MinecraftAccount account } menuItem)
            return;

        _ = RefreshAccountAsync(account, menuItem);
    }

    private async Task RefreshAccountAsync(MinecraftAccount account, Control target)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        topLevel.Notice("正在更新中");

        try
        {
            MinecraftAccount? refreshed = null;

            if (account.AccountType == AccountType.Microsoft)
            {
                refreshed = await AccountRefresher.RefreshMicrosoft(account);
            }
            else if (account.AccountType == AccountType.Yggdrasil)
            {
                refreshed = await AccountRefresher.RefreshYggdrasil(account, Data.ConfigEntry.MinecraftAccounts);
            }

            if (refreshed != null)
            {
                var index = Data.ConfigEntry.MinecraftAccounts.IndexOf(account);
                if (index >= 0)
                {
                    Data.ConfigEntry.MinecraftAccounts[index] = refreshed;
                }

                Data.ConfigEntry.UsingMinecraftMinecraftAccount = refreshed;

                topLevel.Notice("账户信息已更新", NotificationType.Success);
            }
            else
            {
                topLevel.Notice("更新失败", NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            topLevel.Notice($"更新失败：{ex.Message}", NotificationType.Error);
        }
    }

    private void Rename_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: MinecraftAccount account })
            return;

        _ = RenameAccountAsync(account, this);
    }

    private async Task RenameAccountAsync(MinecraftAccount account, Control target)
    {
        var hostId = target.TryGetHostId();
        var result = await RenameOfflineAccountDialog.Show(account, hostId);

        if (result != null)
        {
            account.Name = result.Name;
            account.Uuid = result.Uuid;
            Data.ConfigEntry.UsingMinecraftMinecraftAccount = account;
        }
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: MinecraftAccount account })
            return;

        if (Data.ConfigEntry.UsingMinecraftMinecraftAccount == account)
        {
            Data.ConfigEntry.MinecraftAccounts.Remove(account);
            Data.ConfigEntry.UsingMinecraftMinecraftAccount = Data.ConfigEntry.MinecraftAccounts.FirstOrDefault();
        }
        else
        {
            Data.ConfigEntry.MinecraftAccounts.Remove(account);
        }

        this.AsTopLevel().Notice(new NotificationOptions()
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
    }

    private async void AddAccountClick(object? sender, RoutedEventArgs e)
    {
        var tryGetHostId = this.TryGetHostId()!;
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

    private async void Renote_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: MinecraftAccount account })
            return;

        var hostId = this.TryGetHostId()!;
        var result = await EditAccountNoteDialog.Show(hostId, account.AccountNote);

        if (result != null)
        {
            account.AccountNote = result;
        }
    }

    private async void PreviewSkin_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: MinecraftAccount account })
            return;

        var hostId = this.TryGetHostId()!;
        var skinPath = await ChangeSkinDialog.Preview(hostId, account);
    }

    private async void ChangeSkin_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: MinecraftAccount account })
            return;

        var hostId = this.TryGetHostId()!;
        var newSkinPath = await ChangeSkinDialog.Show(hostId, null);

        if (!string.IsNullOrEmpty(newSkinPath) && System.IO.File.Exists(newSkinPath))
        {
            account.Skin = Convert.ToBase64String(await System.IO.File.ReadAllBytesAsync(newSkinPath));
            this.AsTopLevel().Notice("皮肤已更新", NotificationType.Success);
        }
    }
}
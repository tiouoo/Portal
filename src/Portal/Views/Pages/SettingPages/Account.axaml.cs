using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Operations.Account;
using Portal.Module.AggregatedSearch;
using Portal.Module.Initialize;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Classes;
using TioUi.Common.Extensions;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("账户档案", "设置/账户档案", "Account")]
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
            if (account.AccountType == AccountType.Microsoft)
            {
                var refreshed = await AccountRefresher.RefreshMicrosoft(account);
                if (refreshed == null)
                {
                    topLevel.Notice("更新失败", NotificationType.Error);
                    return;
                }

                var index = Data.ConfigEntry.MinecraftAccounts.IndexOf(account);
                if (index >= 0)
                {
                    Data.ConfigEntry.MinecraftAccounts[index] = refreshed;
                }

                Data.ConfigEntry.UsingMinecraftMinecraftAccount = refreshed;
                topLevel.Notice("账户信息已更新", NotificationType.Success);
            }
            else if (account.AccountType == AccountType.Yggdrasil)
            {
                var result = await AccountRefresher.RefreshYggdrasil(account, Data.ConfigEntry.MinecraftAccounts);
                if (result == null)
                {
                    topLevel.Notice("重新登录失败", NotificationType.Error);
                    return;
                }

                var usingAccount = Data.ConfigEntry.UsingMinecraftMinecraftAccount;
                var usingAccountUuid = usingAccount?.Uuid;
                foreach (var existing in result.Existing)
                {
                    Data.ConfigEntry.MinecraftAccounts.Remove(existing);
                }

                foreach (var refreshed in result.Refreshed)
                {
                    Data.ConfigEntry.MinecraftAccounts.Add(refreshed);
                }

                if (result.Existing.Contains(usingAccount))
                {
                    Data.ConfigEntry.UsingMinecraftMinecraftAccount = usingAccountUuid.HasValue
                        ? result.Refreshed.FirstOrDefault(refreshed => refreshed.Uuid == usingAccountUuid)
                        : null;
                }

                var changes = new List<string>();
                if (result.Added.Count > 0)
                    changes.Add($"新增：{string.Join("、", result.Added.Select(item => item.Name))}");
                if (result.Removed.Count > 0)
                    changes.Add($"删除：{string.Join("、", result.Removed.Select(item => item.Name))}");
                if (result.Updated.Count > 0)
                    changes.Add($"更新：{string.Join("、", result.Updated.Select(item => item.Name))}");

                topLevel.Notice(
                    changes.Count == 0 ? "重新登录完成，账户未变化" : string.Join("\n", changes),
                    NotificationType.Success);
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

    private async void CopyUuid_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: MinecraftAccount account })
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        if (account.Uuid is not { } uuid)
        {
            topLevel.Notice("此账户没有 uuid", NotificationType.Warning);
            return;
        }

        var uuidText = uuid.ToString().ToLowerInvariant();
        if (topLevel.Clipboard is not { } clipboard)
            return;

        await clipboard.SetTextAsync(uuidText);
        topLevel.Notice($"复制成功：{uuidText}", NotificationType.Success);
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
        if (result == null) return;
        foreach (var minecraftAccount in result.JavaAccounts)
        {
            Data.ConfigEntry.MinecraftAccounts.Add(minecraftAccount);
        }
        if (result.JavaAccounts.Count > 0)
            Data.ConfigEntry.UsingMinecraftMinecraftAccount = result.JavaAccounts[^1];
        if (result.BedrockAccount is { } bedrockAccount)
        {
            var existing = Data.ConfigEntry.BedrockAccounts.FirstOrDefault(item => item.Xuid == bedrockAccount.Xuid);
            if (existing != null) Data.ConfigEntry.BedrockAccounts.Remove(existing);
            Data.ConfigEntry.BedrockAccounts.Add(bedrockAccount);
            Data.ConfigEntry.UsingBedrockAccount = bedrockAccount;
        }
    }

    private async void RefreshBedrock_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: BedrockAccount account }) return;
        var topLevel = TopLevel.GetTopLevel(this);
        try
        {
            var refreshed = await new BedrockAuthenticationService().RefreshAsync(account);
            var index = Data.ConfigEntry.BedrockAccounts.IndexOf(account);
            if (index >= 0) Data.ConfigEntry.BedrockAccounts[index] = refreshed;
            Data.ConfigEntry.UsingBedrockAccount = refreshed;
            topLevel?.Notice("基岩账户信息已更新", NotificationType.Success);
        }
        catch (Exception exception)
        {
            topLevel?.Notice($"更新失败：{exception.Message}", NotificationType.Error);
        }
    }

    private async void RenoteBedrock_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: BedrockAccount account }) return;
        var result = await EditAccountNoteDialog.Show(this.TryGetHostId()!, account.AccountNote ?? string.Empty);
        if (result != null) account.AccountNote = result;
    }

    private void RemoveBedrock_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: BedrockAccount account }) return;
        Data.ConfigEntry.BedrockAccounts.Remove(account);
        if (Data.ConfigEntry.UsingBedrockAccount == account)
            Data.ConfigEntry.UsingBedrockAccount = Data.ConfigEntry.BedrockAccounts.FirstOrDefault();
    }

    private async void Renote_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: MinecraftAccount account })
            return;

        var hostId = this.TryGetHostId()!;
        var result = await EditAccountNoteDialog.Show(hostId, account.AccountNote ?? string.Empty);

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

    private void SwitchAvatar_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: MinecraftAccount account })
            return;

        account.UseSimpleAvatar = !account.UseSimpleAvatar;
        ConfigSaver.SaveConfig();
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

using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Portal.Core.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Module.AggregatedSearch;
using Portal.Core.Module.Initialize;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Portal.ViewModels;
using Portal.Views.Components.Operations.Account;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Classes;
using TioUi.Common.Extensions;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("账户档案", "设置/账户档案", "Account")]
public partial class Account : Dsc
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
            new FilePickerSaveOptions
            {
                Title = CommonLanguageManager.Instance.accountPage_saveSkin.CurrentValue(),
                SuggestedFileName = $"{account.Name}.png",
                FileTypeChoices =
                [
                    new FilePickerFileType(CommonLanguageManager.Instance.dashboard_pngImage.CurrentValue())
                    {
                        Patterns = ["*.png"]
                    }
                ]
            });

        if (file == null) return;

        try
        {
            var skinBytes = Convert.FromBase64String(account.Skin);
            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(skinBytes);
            topLevel.Notice(CommonLanguageManager.Instance.accountPage_skinSaved.CurrentValue(),
                NotificationType.Success);
        }
        catch (Exception ex)
        {
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.accountPage_saveFailed.CurrentValue(),
                ex.Message), NotificationType.Error);
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

        topLevel.Notice(CommonLanguageManager.Instance.accountPage_updating.CurrentValue());

        try
        {
            if (account.AccountType == AccountType.Microsoft)
            {
                var refreshed = await AccountRefresher.RefreshMicrosoft(account);
                if (refreshed == null)
                {
                    topLevel.Notice(CommonLanguageManager.Instance.accountPage_updateFailed.CurrentValue(),
                        NotificationType.Error);
                    return;
                }

                var index = Data.ConfigEntry.MinecraftAccounts.IndexOf(account);
                if (index >= 0) Data.ConfigEntry.MinecraftAccounts[index] = refreshed;

                Data.ConfigEntry.UsingMinecraftMinecraftAccount = refreshed;
                topLevel.Notice(CommonLanguageManager.Instance.accountPage_accountUpdated.CurrentValue(),
                    NotificationType.Success);
            }
            else if (account.AccountType == AccountType.Yggdrasil)
            {
                var result = await AccountRefresher.RefreshYggdrasil(account, Data.ConfigEntry.MinecraftAccounts);
                if (result == null)
                {
                    topLevel.Notice(CommonLanguageManager.Instance.accountPage_reloginFailed.CurrentValue(),
                        NotificationType.Error);
                    return;
                }

                var usingAccount = Data.ConfigEntry.UsingMinecraftMinecraftAccount;
                var usingAccountUuid = usingAccount?.Uuid;
                foreach (var existing in result.Existing) Data.ConfigEntry.MinecraftAccounts.Remove(existing);

                foreach (var refreshed in result.Refreshed) Data.ConfigEntry.MinecraftAccounts.Add(refreshed);

                if (result.Existing.Contains(usingAccount))
                    Data.ConfigEntry.UsingMinecraftMinecraftAccount = usingAccountUuid.HasValue
                        ? result.Refreshed.FirstOrDefault(refreshed => refreshed.Uuid == usingAccountUuid)
                        : null;

                var changes = new List<string>();
                if (result.Added.Count > 0)
                    changes.Add(string.Format(
                        CommonLanguageManager.Instance.accountPage_changesAdded.CurrentValue(),
                        string.Join("、", result.Added.Select(item => item.Name))));
                if (result.Removed.Count > 0)
                    changes.Add(string.Format(
                        CommonLanguageManager.Instance.accountPage_changesRemoved.CurrentValue(),
                        string.Join("、", result.Removed.Select(item => item.Name))));
                if (result.Updated.Count > 0)
                    changes.Add(string.Format(
                        CommonLanguageManager.Instance.accountPage_changesUpdated.CurrentValue(),
                        string.Join("、", result.Updated.Select(item => item.Name))));

                topLevel.Notice(
                    changes.Count == 0
                        ? CommonLanguageManager.Instance.accountPage_reloginNoChanges.CurrentValue()
                        : string.Join("\n", changes),
                    NotificationType.Success);
            }
        }
        catch (Exception ex)
        {
            topLevel.Notice(string.Format(
                CommonLanguageManager.Instance.accountPage_updateFailedWithMessage.CurrentValue(), ex.Message),
                NotificationType.Error);
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
            topLevel.Notice(CommonLanguageManager.Instance.accountPage_noUuid.CurrentValue(), NotificationType.Warning);
            return;
        }

        var uuidText = uuid.ToString().ToLowerInvariant();
        if (topLevel.Clipboard is not { } clipboard)
            return;

        await clipboard.SetTextAsync(uuidText);
        topLevel.Notice(string.Format(CommonLanguageManager.Instance.accountPage_copied.CurrentValue(), uuidText),
            NotificationType.Success);
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

        this.AsTopLevel().Notice(new NotificationOptions
        {
            Content = string.Format(CommonLanguageManager.Instance.titleBar_removedAccount.CurrentValue(),
                account.Name, account.DisplayAccountNote),
            Type = NotificationType.Success,
            Expiration = TimeSpan.FromSeconds(3),
            OperateButtons =
            [
                new OperateButtonEntry(CommonLanguageManager.Instance.titleBar_undo.CurrentValue(), _ =>
                {
                    Data.ConfigEntry.MinecraftAccounts.Add(account);
                    Data.ConfigEntry.UsingMinecraftMinecraftAccount = account;
                }, true)
            ]
        });
    }

    private async void AddAccountClick(object? sender, RoutedEventArgs e)
    {
        var tryGetHostId = this.TryGetHostId()!;
        var result = await AddAccount.Main(tryGetHostId, Data.ConfigEntry.AuthServers);
        if (result == null) return;
        foreach (var minecraftAccount in result.JavaAccounts) Data.ConfigEntry.MinecraftAccounts.Add(minecraftAccount);
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
            topLevel?.Notice(CommonLanguageManager.Instance.accountPage_bedrockAccountUpdated.CurrentValue(),
                NotificationType.Success);
        }
        catch (Exception exception)
        {
            topLevel?.Notice(string.Format(
                CommonLanguageManager.Instance.accountPage_updateFailedWithMessage.CurrentValue(),
                exception.Message), NotificationType.Error);
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

        if (result != null) account.AccountNote = result;
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

        if (!string.IsNullOrEmpty(newSkinPath) && File.Exists(newSkinPath))
        {
            account.Skin = Convert.ToBase64String(await File.ReadAllBytesAsync(newSkinPath));
            this.AsTopLevel().Notice(CommonLanguageManager.Instance.accountPage_skinUpdated.CurrentValue(),
                NotificationType.Success);
        }
    }
}
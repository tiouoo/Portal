using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Portal.Core.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using Portal.Core.Module.Initialize;
using Portal.Localization;
using Portal.Module;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Classes;
using TioUi.Common.Extensions;

namespace Portal.Views.Components.Operations.Account;

public static class MinecraftAccountMenu
{
    public static IReadOnlyList<MenuItem> CreateOperationItems(Control owner, MinecraftAccount account,
        Action? changed = null)
    {
        var items = new List<MenuItem>
        {
            CreateItem(SettingsLanguageManager.Instance.account_saveSkin.CurrentValue(), "\ue632",
                () => _ = SaveSkinAsync(owner, account)),
            CreateItem(SettingsLanguageManager.Instance.account_previewSkin.CurrentValue(), "\ue63e",
                () => _ = PreviewSkinAsync(owner, account)),
            CreateItem(SettingsLanguageManager.Instance.account_switchAvatar.CurrentValue(), "\ue621", () =>
            {
                account.UseSimpleAvatar = !account.UseSimpleAvatar;
                ConfigSaver.SaveConfig();
                changed?.Invoke();
            })
        };

        if (account.AccountType == AccountType.Offline)
        {
            items.Add(CreateItem(SettingsLanguageManager.Instance.account_editInfo.CurrentValue(), "\ue62c",
                () => _ = RenameAsync(owner, account, changed)));
        }
        else
        {
            items.Add(CreateItem(SettingsLanguageManager.Instance.account_refreshInfo.CurrentValue(), "\ue63c",
                () => _ = RefreshAsync(owner, account, changed)));
        }

        items.Add(CreateItem(SettingsLanguageManager.Instance.account_editNote.CurrentValue(), "\ue635",
            () => _ = EditNoteAsync(owner, account, changed)));
        items.Add(CreateItem(SettingsLanguageManager.Instance.account_copyUuid.CurrentValue(), "\ue62a",
            () => _ = CopyUuidAsync(owner, account)));
        items.Add(CreateItem(SettingsLanguageManager.Instance.account_removeAccount.CurrentValue(), "\ue640",
            () => Remove(owner, account, changed)));
        return items;
    }

    private static MenuItem CreateItem(string header, string glyph, Action action)
    {
        var item = new MenuItem
        {
            Header = header,
            Icon = IconResources.CreateIcon(glyph, 16),
            Cursor = new Cursor(StandardCursorType.Arrow)
        };
        item.Click += (_, _) => action();
        return item;
    }

    private static async Task SaveSkinAsync(Control owner, MinecraftAccount account)
    {
        var topLevel = TopLevel.GetTopLevel(owner);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
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
        catch (Exception exception)
        {
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.accountPage_saveFailed.CurrentValue(),
                exception.Message), NotificationType.Error);
        }
    }

    private static async Task PreviewSkinAsync(Control owner, MinecraftAccount account)
    {
        await ChangeSkinDialog.Preview(owner.TryGetHostId()!, account);
    }

    private static async Task RenameAsync(Control owner, MinecraftAccount account, Action? changed)
    {
        var useCurrent = Data.ConfigEntry.UsingMinecraftMinecraftAccount?.Equals(account) == true;
        var result = await RenameOfflineAccountDialog.Show(account, owner.TryGetHostId());
        if (result == null) return;

        account.Name = result.Name;
        account.Uuid = result.Uuid;
        if (useCurrent) Data.ConfigEntry.UsingMinecraftMinecraftAccount = account;
        ConfigSaver.SaveConfig();
        changed?.Invoke();
    }

    private static async Task EditNoteAsync(Control owner, MinecraftAccount account, Action? changed)
    {
        var result = await EditAccountNoteDialog.Show(owner.TryGetHostId()!, account.AccountNote ?? string.Empty);
        if (result == null) return;

        account.AccountNote = result;
        ConfigSaver.SaveConfig();
        changed?.Invoke();
    }

    private static async Task CopyUuidAsync(Control owner, MinecraftAccount account)
    {
        var topLevel = TopLevel.GetTopLevel(owner);
        if (topLevel == null) return;
        if (account.Uuid is not { } uuid)
        {
            topLevel.Notice(CommonLanguageManager.Instance.accountPage_noUuid.CurrentValue(),
                NotificationType.Warning);
            return;
        }

        var uuidText = uuid.ToString().ToLowerInvariant();
        if (topLevel.Clipboard is not { } clipboard) return;
        await clipboard.SetTextAsync(uuidText);
        topLevel.Notice(string.Format(CommonLanguageManager.Instance.accountPage_copied.CurrentValue(), uuidText),
            NotificationType.Success);
    }

    private static async Task RefreshAsync(Control owner, MinecraftAccount account, Action? changed)
    {
        var topLevel = TopLevel.GetTopLevel(owner);
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

                ReplaceAccount(account, refreshed);
                topLevel.Notice(CommonLanguageManager.Instance.accountPage_accountUpdated.CurrentValue(),
                    NotificationType.Success);
            }
            else if (account.AccountType == AccountType.Yggdrasil)
            {
                await RefreshYggdrasilAsync(topLevel, account);
            }

            changed?.Invoke();
        }
        catch (Exception exception)
        {
            topLevel.Notice(string.Format(
                    CommonLanguageManager.Instance.accountPage_updateFailedWithMessage.CurrentValue(),
                    exception.Message), NotificationType.Error);
        }
    }

    private static async Task RefreshYggdrasilAsync(TopLevel topLevel, MinecraftAccount account)
    {
        var result = await AccountRefresher.RefreshYggdrasil(account, Data.ConfigEntry.MinecraftAccounts);
        if (result == null)
        {
            topLevel.Notice(CommonLanguageManager.Instance.accountPage_reloginFailed.CurrentValue(),
                NotificationType.Error);
            return;
        }

        var current = Data.ConfigEntry.UsingMinecraftMinecraftAccount;
        foreach (var existing in result.Existing)
            Data.ConfigEntry.MinecraftAccounts.Remove(existing);
        foreach (var refreshed in result.Refreshed)
            Data.ConfigEntry.MinecraftAccounts.Add(refreshed);

        Data.ConfigEntry.UsingMinecraftMinecraftAccount = ResolveRefreshed(current, result.Refreshed);

        var changes = new List<string>();
        if (result.Added.Count > 0)
            changes.Add(string.Format(CommonLanguageManager.Instance.accountPage_changesAdded.CurrentValue(),
                string.Join("、", result.Added.Select(item => item.Name))));
        if (result.Removed.Count > 0)
            changes.Add(string.Format(CommonLanguageManager.Instance.accountPage_changesRemoved.CurrentValue(),
                string.Join("、", result.Removed.Select(item => item.Name))));
        if (result.Updated.Count > 0)
            changes.Add(string.Format(CommonLanguageManager.Instance.accountPage_changesUpdated.CurrentValue(),
                string.Join("、", result.Updated.Select(item => item.Name))));

        topLevel.Notice(changes.Count == 0
                ? CommonLanguageManager.Instance.accountPage_reloginNoChanges.CurrentValue()
                : string.Join("\n", changes),
            NotificationType.Success);
    }

    private static MinecraftAccount? ResolveRefreshed(MinecraftAccount? previous,
        IReadOnlyCollection<MinecraftAccount> refreshed)
    {
        if (previous == null) return null;
        return refreshed.FirstOrDefault(item => item.Equals(previous)) ??
               Data.ConfigEntry.MinecraftAccounts.FirstOrDefault(item => item.Equals(previous));
    }

    private static void ReplaceAccount(MinecraftAccount original, MinecraftAccount refreshed)
    {
        var useCurrent = Data.ConfigEntry.UsingMinecraftMinecraftAccount?.Equals(original) == true;
        var index = Data.ConfigEntry.MinecraftAccounts.IndexOf(original);
        if (index >= 0) Data.ConfigEntry.MinecraftAccounts[index] = refreshed;
        if (useCurrent) Data.ConfigEntry.UsingMinecraftMinecraftAccount = refreshed;
    }

    private static void Remove(Control owner, MinecraftAccount account, Action? changed)
    {
        var useCurrent = Data.ConfigEntry.UsingMinecraftMinecraftAccount?.Equals(account) == true;
        Data.ConfigEntry.MinecraftAccounts.Remove(account);
        if (useCurrent)
            Data.ConfigEntry.UsingMinecraftMinecraftAccount = Data.ConfigEntry.MinecraftAccounts.FirstOrDefault();
        changed?.Invoke();

        owner.AsTopLevel().Notice(new NotificationOptions
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
                    if (useCurrent) Data.ConfigEntry.UsingMinecraftMinecraftAccount = account;
                    changed?.Invoke();
                }, true)
            ]
        });
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft.Classes;
using TioUi.Common;
using TioUi.Common.Interfaces;
using TioUi.Controls;

namespace Portal.Core.Operations.Account;

public partial class RenameOfflineAccount : UserControl
{
    public RenameOfflineAccount()
    {
        InitializeComponent();
    }
}

public static class RenameOfflineAccountDialog
{
    public static async Task<MinecraftAccount?> Show(MinecraftAccount account, string? hostId)
    {
        var options = new OverlayDialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            CanResize = false,
            VerticalAnchor = VerticalPosition.Center
        };

        var result = await OverlayDialog.ShowCustomAsync<RenameOfflineAccount, RenameOfflineAccountViewModel, MinecraftAccount>(
            new RenameOfflineAccountViewModel(account), hostId: hostId, options: options);

        return result;
    }
}

public partial class RenameOfflineAccountViewModel : ObservableObject, IDialogContext, INotifyDataErrorInfo
{
    private readonly MinecraftAccount _originalAccount;

    [ObservableProperty]
    public partial string? RoleName { get; set; }

    [ObservableProperty]
    public partial string? Uuid { get; set; }

    [ObservableProperty]
    public partial bool SyncUuid { get; set; }

    [ObservableProperty]
    public partial bool IgnoreStandard { get; set; }

    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    private readonly Dictionary<string, List<string>> _errors = new();

    public RenameOfflineAccountViewModel(MinecraftAccount account)
    {
        _originalAccount = account;
        RoleName = account.Name;
        Uuid = account.Uuid?.ToString() ?? MinecraftAccount.GetMinecraftOfflineUuid(account.Name).ToString();
        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
        CancelCommand = new RelayCommand(Cancel);
    }

    partial void OnRoleNameChanged(string? value)
    {
        ValidateRoleName(value);

        if (SyncUuid)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Uuid = string.Empty;
            }
            else
            {
                Uuid = MinecraftAccount.GetMinecraftOfflineUuid(value).ToString();
            }
        }

        (ConfirmCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    partial void OnUuidChanged(string? value)
    {
        ValidateUuid(value);
        (ConfirmCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    partial void OnSyncUuidChanged(bool value)
    {
        if (value && !string.IsNullOrWhiteSpace(RoleName))
        {
            Uuid = MinecraftAccount.GetMinecraftOfflineUuid(RoleName).ToString();
        }

        ValidateRoleName(RoleName);
        ValidateUuid(Uuid);
        (ConfirmCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    partial void OnIgnoreStandardChanged(bool value)
    {
        ValidateRoleName(RoleName);
        (ConfirmCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void ValidateRoleName(string? value)
    {
        var propertyName = nameof(RoleName);

        if (_errors.ContainsKey(propertyName))
        {
            _errors.Remove(propertyName);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            _errors[propertyName] = new List<string> { "玩家名称不能为空" };
        }
        else if (!IgnoreStandard)
        {
            if (value.Length < 3 || value.Length > 15)
            {
                _errors[propertyName] = new List<string> { "玩家名称必须为3~15位字符" };
            }
            else if (!Regex().IsMatch(value))
            {
                _errors[propertyName] = new List<string> { "玩家名称只能包含数字、大小写字母和下划线" };
            }
        }

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }

    private void ValidateUuid(string? value)
    {
        var propertyName = nameof(Uuid);

        if (_errors.ContainsKey(propertyName))
        {
            _errors.Remove(propertyName);
        }

        if (SyncUuid && !string.IsNullOrWhiteSpace(value) && !IsValidUuid(value))
        {
            _errors[propertyName] = new List<string> { "UUID 格式不正确" };
        }

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }

    private bool IsValidUuid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        return Guid.TryParse(value, out _);
    }

    private bool CanConfirm()
    {
        if (HasErrors)
            return false;

        if (string.IsNullOrWhiteSpace(RoleName))
            return false;

        if (SyncUuid && string.IsNullOrWhiteSpace(Uuid))
            return false;

        return true;
    }

    private void Confirm()
    {
        if (!CanConfirm())
            return;

        Guid uuid;
        if (SyncUuid && Guid.TryParse(Uuid, out var parsedUuid))
        {
            uuid = parsedUuid;
        }
        else
        {
            uuid = _originalAccount.Uuid ?? MinecraftAccount.GetMinecraftOfflineUuid(RoleName);
        }

        var newAccount = new MinecraftAccount(AccountType.Offline)
        {
            Name = RoleName,
            Uuid = uuid,
            CreateAt = _originalAccount.CreateAt,
            LastLoginTime = _originalAccount.LastLoginTime,
            LastRefreshTime = _originalAccount.LastRefreshTime,
            Skin = _originalAccount.Skin,
            AccountNote = _originalAccount.AccountNote,
        };

        RequestClose?.Invoke(this, newAccount);
    }

    private void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }

    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;

    public bool HasErrors => _errors.Count > 0;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName) || !_errors.ContainsKey(propertyName))
        {
            return Enumerable.Empty<string>();
        }
        return _errors[propertyName];
    }

    [GeneratedRegex(@"^[a-zA-Z0-9_]+$")]
    private static partial Regex Regex();
}

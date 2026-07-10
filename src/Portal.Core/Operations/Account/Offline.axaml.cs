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
using Portal.Core.Minecraft.Account;
using TioUi.Common.Interfaces;

namespace Portal.Core.Operations.Account;

public partial class Offline : UserControl
{
    public Offline()
    {
        InitializeComponent();
    }
}

public partial class OfflineAccountViewModel : ObservableObject, IDialogContext, INotifyDataErrorInfo
{
    [ObservableProperty]
    public partial string? RoleName { get; set; }
    [ObservableProperty]
    public partial string? Uuid { get; set; }
    [ObservableProperty]
    public partial bool IgnoreStandard { get; set; }
    public ICommand NextCommand { get; }
    public ICommand CancelCommand { get; }

    private readonly Dictionary<string, List<string>> _errors = new();

    public OfflineAccountViewModel()
    {
        NextCommand = new RelayCommand(Next, CanNext);
        CancelCommand = new RelayCommand(Cancel);
    }

    partial void OnRoleNameChanged(string? value)
    {
        ValidateRoleName(value);
        
        if (string.IsNullOrWhiteSpace(value))
        {
            Uuid = string.Empty;
        }
        else
        {
            Uuid = MinecraftAccount.GetMinecraftOfflineUuid(value).ToString();
        }
        
        (NextCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    partial void OnUuidChanged(string? value)
    {
        ValidateUuid(value);
        (NextCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    partial void OnIgnoreStandardChanged(bool value)
    {
        ValidateRoleName(RoleName);
        (NextCommand as RelayCommand)?.NotifyCanExecuteChanged();
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

        if (!string.IsNullOrWhiteSpace(value) && !IsValidUuid(value))
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

    private bool CanNext()
    {
        return !HasErrors && !string.IsNullOrWhiteSpace(RoleName) && !string.IsNullOrWhiteSpace(Uuid);
    }

    private void Next()
    {
        if (Guid.TryParse(Uuid, out var uuid))
        {
            RequestClose?.Invoke(this, new MinecraftAccount(AccountType.Offline)
            {
                Name = RoleName,
                Uuid = uuid
            });
        }
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
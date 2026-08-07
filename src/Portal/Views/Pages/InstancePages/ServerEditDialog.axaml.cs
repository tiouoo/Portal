using System.Collections;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft.Services;
using TioUi.Common;
using TioUi.Common.Interfaces;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class ServerEditDialog : UserControl
{
    public ServerEditDialog(string title, string name, string address)
    {
        InitializeComponent();
        DataContext = new ServerEditDialogViewModel(title, name, address);
    }
}

public static class ServerEditDialogHelper
{
    public static async Task<ServerEditResult?> ShowAsync(string title, string name, string address, string? hostId)
    {
        var dialog = new ServerEditDialog(title, name, address);
        var options = new OverlayDialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            CanResize = false,
            VerticalAnchor = VerticalPosition.Top,
            VerticalOffset = 110
        };

        return await OverlayDialog.ShowCustomAsync<ServerEditResult?>(dialog, dialog.DataContext, hostId: hostId,
            options: options);
    }
}

public sealed record ServerEditResult(string Name, string Address);

public partial class ServerEditDialogViewModel : ObservableObject, IDialogContext, INotifyDataErrorInfo
{
    private readonly Dictionary<string, List<string>> _errors = [];

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Address { get; set; }

    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    public ServerEditDialogViewModel(string title, string name, string address)
    {
        Title = title;
        Name = name;
        Address = address;
        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
        CancelCommand = new RelayCommand(Cancel);
        Validate();
    }

    partial void OnNameChanged(string value)
    {
        Validate();
        (ConfirmCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    partial void OnAddressChanged(string value)
    {
        Validate();
        (ConfirmCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void Validate()
    {
        ValidateName();
        ValidateAddress();
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(Name)));
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(Address)));
    }

    private void ValidateName()
    {
        _errors.Remove(nameof(Name));
        if (string.IsNullOrWhiteSpace(Name))
            _errors[nameof(Name)] = ["服务器名称不能为空"];
        else if (Name.Length > 100)
            _errors[nameof(Name)] = ["服务器名称过长（最多 100 个字符）"];
    }

    private void ValidateAddress()
    {
        _errors.Remove(nameof(Address));
        var value = Address?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            _errors[nameof(Address)] = ["服务器地址不能为空"];
            return;
        }

        if (value.Length > 255)
        {
            _errors[nameof(Address)] = ["服务器地址过长（最多 255 个字符）"];
            return;
        }

        var (host, port) = JavaServerManager.ParseAddress(value);
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
            _errors[nameof(Address)] = ["服务器地址格式无效"];
    }

    private bool CanConfirm() => _errors.Count == 0;

    private void Confirm()
    {
        if (!CanConfirm())
            return;

        RequestClose?.Invoke(this, new ServerEditResult(Name!.Trim(), Address!.Trim()));
    }

    private void Cancel() => RequestClose?.Invoke(this, null);

    public void Close() => Cancel();

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
}

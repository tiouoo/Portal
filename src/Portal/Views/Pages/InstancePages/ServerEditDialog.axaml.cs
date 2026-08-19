using System.Collections;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using TioUi.Common;
using TioUi.Common.Interfaces;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class ServerEditDialog : UserControl
{
    public ServerEditDialog()
    {
        InitializeComponent();
    }

    public ServerEditDialog(string title, string name, string address, int defaultPort = 25565)
    {
        InitializeComponent();
        DataContext = new ServerEditDialogViewModel(title, name, address, defaultPort);
    }
}

public static class ServerEditDialogHelper
{
    public static async Task<ServerEditResult?> ShowAsync(string title, string name, string address, string? hostId,
        int defaultPort = 25565)
    {
        var dialog = new ServerEditDialog(title, name, address, defaultPort);
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

        return await OverlayDialog.ShowCustomAsync<ServerEditResult?>(dialog, dialog.DataContext, hostId,
            options);
    }
}

public sealed record ServerEditResult(string Name, string Address);

public partial class ServerEditDialogViewModel : ObservableObject, IDialogContext, INotifyDataErrorInfo
{
    private readonly int _defaultPort;
    private readonly Dictionary<string, List<string>> _errors = [];

    public ServerEditDialogViewModel(string title, string name, string address, int defaultPort)
    {
        Title = title;
        Name = name;
        Address = address;
        _defaultPort = defaultPort;
        AddressPlaceholder = string.Format(
            CommonLanguageManager.Instance.serverEdit_addressPlaceholder.CurrentValue(), defaultPort);
        PortHint = string.Format(CommonLanguageManager.Instance.serverEdit_portHint.CurrentValue(), defaultPort);
        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
        CancelCommand = new RelayCommand(Cancel);
        Validate();
    }

    [ObservableProperty] public partial string Title { get; set; }

    [ObservableProperty] public partial string Name { get; set; }

    [ObservableProperty] public partial string Address { get; set; }

    public string AddressPlaceholder { get; }
    public string PortHint { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    public void Close()
    {
        Cancel();
    }

    public event EventHandler<object?>? RequestClose;

    public bool HasErrors => _errors.Count > 0;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName) || !_errors.ContainsKey(propertyName)) return Enumerable.Empty<string>();

        return _errors[propertyName];
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
            _errors[nameof(Name)] = [CommonLanguageManager.Instance.account_serverNameEmpty.CurrentValue()];
        else if (Name.Length > 100)
            _errors[nameof(Name)] = [CommonLanguageManager.Instance.serverEdit_nameTooLong.CurrentValue()];
    }

    private void ValidateAddress()
    {
        _errors.Remove(nameof(Address));
        var value = Address?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            _errors[nameof(Address)] = [CommonLanguageManager.Instance.serverEdit_addressEmpty.CurrentValue()];
            return;
        }

        if (value.Length > 255)
        {
            _errors[nameof(Address)] = [CommonLanguageManager.Instance.serverEdit_addressTooLong.CurrentValue()];
            return;
        }

        var (host, port) = _defaultPort == BedrockServerManager.DefaultPort
            ? BedrockServerManager.ParseAddress(value)
            : JavaServerManager.ParseAddress(value);
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
            _errors[nameof(Address)] = [CommonLanguageManager.Instance.serverEdit_addressInvalid.CurrentValue()];
    }

    private bool CanConfirm()
    {
        return _errors.Count == 0;
    }

    private void Confirm()
    {
        if (!CanConfirm())
            return;

        RequestClose?.Invoke(this, new ServerEditResult(Name!.Trim(), Address!.Trim()));
    }

    private void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }
}
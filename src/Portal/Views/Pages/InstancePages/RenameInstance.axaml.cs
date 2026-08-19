using System.Collections;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft.Classes;
using Portal.Localization;
using TioUi.Common;
using TioUi.Common.Interfaces;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class RenameInstance : UserControl
{
    public RenameInstance()
    {
        InitializeComponent();
    }

    public RenameInstance(MinecraftInstance instance)
    {
        InitializeComponent();
        DataContext = new RenameInstanceDialogViewModel(instance);
    }
}

public static class RenameInstanceDialog
{
    public static async Task<string?> Show(MinecraftInstance instance, string? hostId)
    {
        var dialog = new RenameInstance(instance);
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

        return await OverlayDialog.ShowCustomAsync<string?>(dialog, dialog.DataContext, hostId, options);
    }
}

public partial class RenameInstanceDialogViewModel : ObservableObject, IDialogContext, INotifyDataErrorInfo
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "CONIN$", "CONOUT$", "CLOCK$"
    };

    private readonly Dictionary<string, List<string>> _errors = [];
    private readonly MinecraftInstance _instance;

    public RenameInstanceDialogViewModel(MinecraftInstance instance)
    {
        _instance = instance;
        NewId = instance.MinecraftEntry?.Id;
        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
        CancelCommand = new RelayCommand(Cancel);
        ValidateNewId();
    }

    [ObservableProperty] public partial string? NewId { get; set; }

    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;

    public bool HasErrors => _errors.Count > 0;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName) || !_errors.ContainsKey(propertyName)) return Enumerable.Empty<string>();
        return _errors[propertyName];
    }

    partial void OnNewIdChanged(string? value)
    {
        ValidateNewId();
        (ConfirmCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void ValidateNewId()
    {
        var propertyName = nameof(NewId);

        if (_errors.ContainsKey(propertyName)) _errors.Remove(propertyName);

        var value = NewId?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            _errors[propertyName] = [CommonLanguageManager.Instance.instanceRename_nameEmpty.CurrentValue()];
        else if (value.Length > 100)
            _errors[propertyName] = [CommonLanguageManager.Instance.instanceRename_nameTooLong.CurrentValue()];
        else if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            _errors[propertyName] =
                [CommonLanguageManager.Instance.instanceRename_nameInvalidChars.CurrentValue()];
        else if (value.EndsWith(' ') || value.EndsWith('.'))
            _errors[propertyName] = [CommonLanguageManager.Instance.instanceRename_nameEndsWithSpace.CurrentValue()];
        else if (ReservedNames.Contains(value))
            _errors[propertyName] = [CommonLanguageManager.Instance.instanceRename_nameReserved.CurrentValue()];
        else if (string.Equals(value, _instance.MinecraftEntry?.Id, StringComparison.OrdinalIgnoreCase))
            _errors[propertyName] = [CommonLanguageManager.Instance.instanceRename_nameSameAsOriginal.CurrentValue()];
        else if (InstanceFolderExists(value))
            _errors[propertyName] = [CommonLanguageManager.Instance.instanceRename_nameExists.CurrentValue()];

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }

    private bool InstanceFolderExists(string name)
    {
        if (_instance.Layout is not { } layout)
            return true;
        var instancesRoot = Path.GetDirectoryName(layout.InstanceRoot);
        if (instancesRoot is null)
            return true;
        return Directory.Exists(Path.Combine(instancesRoot, name));
    }

    private bool CanConfirm()
    {
        if (HasErrors)
            return false;

        return !string.IsNullOrWhiteSpace(NewId);
    }

    private void Confirm()
    {
        if (!CanConfirm())
            return;

        RequestClose?.Invoke(this, NewId!.Trim());
    }

    private void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }
}
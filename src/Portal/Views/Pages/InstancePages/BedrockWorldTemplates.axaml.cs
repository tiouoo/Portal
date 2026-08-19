using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class BedrockWorldTemplates : UserControl, INotifyPropertyChanged, IDisposable
{
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly MinecraftInstance? _instance;
    private readonly WorldTemplateService _worldTemplateService = new();
    private string _filter = string.Empty;
    private bool _hasLoaded, _isLoading, _isDisposed;

    public BedrockWorldTemplates()
    {
        InitializeComponent();
        DataContext = this;
    }

    public BedrockWorldTemplates(MinecraftInstance instance) : this()
    {
        _instance = instance;
    }

    public ObservableCollection<WorldTemplateItem> Items { get; } = [];
    public ObservableCollection<WorldTemplateItem> FilteredItems { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading != value)
            {
                _isLoading = value;
                RaisePropertyChanged(nameof(IsLoading));
            }
        }
    }

    public bool IsEmpty => !IsLoading && FilteredItems.Count == 0;
    public string CountText => IsLoading
        ? string.Empty
        : string.Format(CommonLanguageManager.Instance.resourceList_count.CurrentValue(), FilteredItems.Count);
    public int SelectedCount => Items.Count(item => item.IsSelected);
    public string SelectedCountText =>
        string.Format(CommonLanguageManager.Instance.resourceList_batchSelected.CurrentValue(), SelectedCount);
    public bool HasMultipleSelection => SelectedCount >= 1;

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _disposeCancellation.Cancel();
        foreach (var item in Items) item.Dispose();
        _disposeCancellation.Dispose();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_hasLoaded || _instance == null) return;
        _hasLoaded = true;
        IsLoading = true;
        RaiseListProperties();
        try
        {
            var templates = await _worldTemplateService.ScanAsync(_instance, _disposeCancellation.Token);
            if (_isDisposed) return;
            foreach (var item in Items) item.Dispose();
            Items.Clear();
            RaiseSelectionProperties();
            foreach (var template in templates) Items.Add(new WorldTemplateItem(template));
            ApplyFilter();
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[WorldTemplates] Template scan cancelled: {exception}");
        }
        finally
        {
            if (!_isDisposed)
            {
                IsLoading = false;
                RaiseListProperties();
            }
        }
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } topLevel && _instance != null)
            await topLevel.Launcher.LaunchDirectoryInfoAsync(
                new DirectoryInfo(_instance.GetSpecialFolder(MinecraftSpecialFolder.WorldTemplatesFolder)));
    }

    private void Title_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _hasLoaded = false;
        _ = LoadAsync();
    }

    private void SearchBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        _filter = (sender as TextBox)?.Text ?? string.Empty;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(_filter)
            ? Items
            : Items.Where(item =>
                item.DisplayName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                item.FileName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                item.DescriptionText.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        FilteredItems.Clear();
        foreach (var item in filtered) FilteredItems.Add(item);
        RaiseListProperties();
    }

    private void WorldTemplateCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Control).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed &&
            (sender as Control)?.DataContext is WorldTemplateItem item)
        {
            item.IsSelected = !item.IsSelected;
            RaiseSelectionProperties();
        }
    }

    private void SelectAll_OnClick(object? sender, RoutedEventArgs e)
    {
        SetSelection(item => true);
    }

    private void ClearSelection_OnClick(object? sender, RoutedEventArgs e)
    {
        SetSelection(item => false);
    }

    private void InvertSelection_OnClick(object? sender, RoutedEventArgs e)
    {
        SetSelection(item => !item.IsSelected);
    }

    private async void DeleteSelected_OnClick(object? sender, RoutedEventArgs e)
    {
        var selected = Items.Where(item => item.IsSelected).ToArray();
        if (selected.Length >= 2 &&
            await ConfirmDeleteAsync(string.Format(
                CommonLanguageManager.Instance.worldTemplates_deleteSelectedConfirm.CurrentValue(),
                selected.Length)) == DialogResult.Yes)
            await DeleteAsync(selected);
    }

    private async void DeleteWorldTemplate_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is { } item &&
            await ConfirmDeleteAsync(string.Format(
                CommonLanguageManager.Instance.worldTemplates_deleteConfirm.CurrentValue(), item.DisplayName)) ==
            DialogResult.Yes)
            await DeleteAsync([item]);
    }

    private async void ShowWorldTemplateDetails_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item) return;
        await OverlayDialog.ShowStandardAsync(
            new TextBlock { Margin = new Thickness(24), Text = item.DetailsText, TextWrapping = TextWrapping.Wrap },
            null, this.TryGetHostId(),
            new OverlayDialogOptions
            {
                Title = CommonLanguageManager.Instance.worldTemplates_detailsTitle.CurrentValue(),
                Buttons = DialogButton.OK, CanResize = false
            });
    }

    private async void OpenWorldTemplateFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item || TopLevel.GetTopLevel(this) is not { } topLevel) return;
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Info.FilePath}\"")
                { UseShellExecute = true });
            return;
        }

        await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(Path.GetDirectoryName(item.Info.FilePath)!));
    }

    private async Task<DialogResult> ConfirmDeleteAsync(string message)
    {
        return await OverlayDialog.ShowStandardAsync(
            new TextBlock { Margin = new Thickness(24), Text = message, TextWrapping = TextWrapping.Wrap }, null,
            this.TryGetHostId(),
            new OverlayDialogOptions
            {
                Title = CommonLanguageManager.Instance.worldTemplates_deleteTitle.CurrentValue(),
                Mode = DialogMode.Error, Buttons = DialogButton.YesNo,
                OverrideYesButtonText = CommonLanguageManager.Instance.dashboard_delete.CurrentValue(),
                OverrideNoButtonText = CommonLanguageManager.Instance.common_cancel.CurrentValue(),
                CanLightDismiss = false, CanResize = false
            });
    }

    private async Task DeleteAsync(IEnumerable<WorldTemplateItem> items)
    {
        var failed = 0;
        foreach (var item in items)
            try
            {
                Logger.Info($"[WorldTemplates] Deleting template {item.DisplayName} at {item.Info.FilePath}.");
                Directory.Delete(item.Info.FilePath, true);
            }
            catch (IOException exception)
            {
                Logger.Warning($"[WorldTemplates] Failed to delete {item.Info.FilePath}: {exception}");
                failed++;
            }
            catch (UnauthorizedAccessException exception)
            {
                Logger.Warning($"[WorldTemplates] Failed to delete {item.Info.FilePath}: {exception}");
                failed++;
            }

        _hasLoaded = false;
        await LoadAsync();
        if (TopLevel.GetTopLevel(this) is { } topLevel)
            topLevel.Notice(failed == 0
                    ? CommonLanguageManager.Instance.worldTemplates_deletedSelected.CurrentValue()
                    : string.Format(CommonLanguageManager.Instance.worldTemplates_deleteFailed.CurrentValue(), failed),
                failed == 0 ? NotificationType.Success : NotificationType.Warning);
    }

    private static WorldTemplateItem? GetItem(object? sender)
    {
        return (sender as Control)?.Tag as WorldTemplateItem;
    }

    private void SetSelection(Func<WorldTemplateItem, bool> selection)
    {
        foreach (var item in Items) item.IsSelected = selection(item);
        RaiseSelectionProperties();
    }

    private void RaiseListProperties()
    {
        RaisePropertyChanged(nameof(IsEmpty));
        RaisePropertyChanged(nameof(CountText));
    }

    private void RaiseSelectionProperties()
    {
        RaisePropertyChanged(nameof(SelectedCount));
        RaisePropertyChanged(nameof(SelectedCountText));
        RaisePropertyChanged(nameof(HasMultipleSelection));
    }

    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class WorldTemplateItem(WorldTemplateInfo info) : INotifyPropertyChanged, IDisposable
{
    private bool _isSelected;
    public WorldTemplateInfo Info { get; } = info;
    public string DisplayName => Info.DisplayName;
    public string FileName => Info.FileName;
    public string DescriptionText =>
        string.IsNullOrWhiteSpace(Info.Description)
            ? CommonLanguageManager.Instance.worldTemplates_noDescription.CurrentValue()
            : Info.Description;
    public string BaseGameVersionText =>
        Info.BaseGameVersion ?? CommonLanguageManager.Instance.account_unknown.CurrentValue();
    public string PackSummary =>
        string.Format(CommonLanguageManager.Instance.bedrockWorlds_packSummary.CurrentValue(),
            Info.ResourcePacks.Count, Info.BehaviorPacks.Count);

    public string DetailsText =>
        string.Format(CommonLanguageManager.Instance.worldTemplates_details.CurrentValue(), DisplayName, FileName,
            Info.Uuid?.ToLowerInvariant() ?? CommonLanguageManager.Instance.account_unknown.CurrentValue(),
            Info.Version ?? CommonLanguageManager.Instance.account_unknown.CurrentValue(), BaseGameVersionText,
            Info.ModuleUuids.Count == 0
                ? CommonLanguageManager.Instance.account_unknown.CurrentValue()
                : string.Join("、", Info.ModuleUuids.Select(uuid => uuid.ToLowerInvariant())),
            FormatPacks(Info.ResourcePacks), FormatPacks(Info.BehaviorPacks), DescriptionText);

    public Bitmap? Icon { get; } = CreateIcon(info.IconData);
    public bool HasIcon => Icon != null;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public void Dispose()
    {
        Icon?.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static string FormatPacks(IReadOnlyList<WorldTemplatePackReference> packs)
    {
        var unknown = CommonLanguageManager.Instance.account_unknown.CurrentValue();
        return packs.Count == 0
            ? CommonLanguageManager.Instance.worldTemplates_none.CurrentValue()
            : string.Join('\n',
                packs.Select(pack => string.Format(
                    CommonLanguageManager.Instance.worldTemplates_packFormat.CurrentValue(), pack.PackId,
                    pack.Subpack ?? CommonLanguageManager.Instance.worldTemplates_defaultPack.CurrentValue(),
                    pack.Version ?? unknown)));
    }

    private static Bitmap? CreateIcon(byte[]? data)
    {
        if (data == null) return null;
        try
        {
            return new Bitmap(new MemoryStream(data));
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
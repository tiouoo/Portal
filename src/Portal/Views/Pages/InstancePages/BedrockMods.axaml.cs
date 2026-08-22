using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Portal.Bedrock.Standard.Manifest;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Const;
using Portal.Localization;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;
using NumericUpDown = Avalonia.Controls.NumericUpDown;

namespace Portal.Views.Pages.InstancePages;

public partial class BedrockMods : UserControl, INotifyPropertyChanged
{
    private readonly MinecraftInstance? _instance;
    private string _filter = string.Empty;
    private ResourceFilterMode _filterMode = ResourceFilterMode.All;
    private bool _hasLoaded, _isLoading;
    private ResourceSortMode _sortMode = ResourceSortMode.FileName;

    public BedrockMods()
    {
        InitializeComponent();
        DataContext = this;
        FilterOptions.Add(new ResourceFilterOption(ResourceListUi.BuildFilterLabel(
            CommonLanguageManager.Instance.mod_all.CurrentValue(), 0)));
        FilterOptions.Add(new ResourceFilterOption(ResourceListUi.BuildFilterLabel(
            CommonLanguageManager.Instance.resourceList_enabled.CurrentValue(), 0)));
        FilterOptions.Add(new ResourceFilterOption(ResourceListUi.BuildFilterLabel(
            CommonLanguageManager.Instance.resourceList_disabled.CurrentValue(), 0)));
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, Resource_OnDragOver);
        AddHandler(DragDrop.DropEvent, Resource_OnDrop);
    }

    public BedrockMods(MinecraftInstance instance) : this()
    {
        _instance = instance;
    }

    public ObservableCollection<BedrockModItem> Items { get; } = [];
    public ObservableCollection<BedrockModItem> FilteredItems { get; } = [];
    public string[] SortOptions => ResourceListUi.SortOptions;
    public ObservableCollection<ResourceFilterOption> FilterOptions { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading != value)
            {
                _isLoading = value;
                Raise(nameof(IsLoading));
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
    public bool HasSelection => SelectedCount > 0;

    private BedrockInstanceConfig? Config => _instance?.BedrockConfig;

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnsureDefaultSelections();
        _ = LoadAsync();
    }

    private void EnsureDefaultSelections()
    {
        if (FilterComboBox.SelectedIndex < 0)
            FilterComboBox.SelectedIndex = 0;
        if (SortComboBox.SelectedIndex < 0)
            SortComboBox.SelectedIndex = Math.Clamp(Data.ConfigEntry.ResourceListSortIndex, 0, SortOptions.Length - 1);
    }

    private void SortComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedIndex: >= 0 } combo)
            return;
        Data.ConfigEntry.ResourceListSortIndex = combo.SelectedIndex;
        _sortMode = combo.SelectedIndex switch
        {
            1 => ResourceSortMode.Name,
            2 => ResourceSortMode.LastWriteTime,
            3 => ResourceSortMode.FileSize,
            _ => ResourceSortMode.FileName
        };
        ApplyFilter();
    }

    private void FilterComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedIndex: >= 0 } combo)
            return;
        _filterMode = combo.SelectedIndex switch
        {
            1 => ResourceFilterMode.Enabled,
            2 => ResourceFilterMode.Disabled,
            3 => ResourceFilterMode.Duplicates,
            _ => ResourceFilterMode.All
        };
        ApplyFilter();
    }

    private async Task LoadAsync()
    {
        if (_hasLoaded || Config == null) return;
        _hasLoaded = true;
        IsLoading = true;
        RaiseList();
        try
        {
            var mods = await Task.Run(() => BedrockModManager.Scan(Config));
            Items.Clear();
            foreach (var mod in mods) Items.Add(new BedrockModItem(mod));
            ApplyFilter();
            RaiseSelection();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or NotSupportedException)
        {
            ShowNotice(string.Format(CommonLanguageManager.Instance.bedrockMods_readFailed.CurrentValue(),
                exception.Message), NotificationType.Error);
        }
        finally
        {
            IsLoading = false;
            RaiseList();
        }
    }

    private async Task RefreshAsync()
    {
        _hasLoaded = false;
        await LoadAsync();
    }

    private void ApplyFilter()
    {
        var query = string.IsNullOrWhiteSpace(_filter)
            ? Items
            : Items.Where(item => item.FileName.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        FilteredItems.Clear();
        foreach (var item in SortItems(query).Where(MatchesStateFilter))
            FilteredItems.Add(item);
        RefreshFilterOptions();
        RaiseList();
    }

    private bool MatchesStateFilter(BedrockModItem item)
    {
        return _filterMode switch
        {
            ResourceFilterMode.Enabled => item.IsEnabled,
            ResourceFilterMode.Disabled => item.IsDisabled,
            _ => true
        };
    }

    private IEnumerable<BedrockModItem> SortItems(IEnumerable<BedrockModItem> source)
    {
        return _sortMode switch
        {
            ResourceSortMode.Name => source.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase),
            ResourceSortMode.LastWriteTime => source.OrderByDescending(item => item.LastWriteTime),
            ResourceSortMode.FileSize => source.OrderByDescending(item => item.Info.FileSize),
            _ => source.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
        };
    }

    private void RefreshFilterOptions()
    {
        FilterOptions[0].Label = ResourceListUi.BuildFilterLabel(CommonLanguageManager.Instance.mod_all.CurrentValue(),
            Items.Count);
        FilterOptions[1].Label = ResourceListUi.BuildFilterLabel(
            CommonLanguageManager.Instance.resourceList_enabled.CurrentValue(), Items.Count(item => item.IsEnabled));
        FilterOptions[2].Label = ResourceListUi.BuildFilterLabel(
            CommonLanguageManager.Instance.resourceList_disabled.CurrentValue(), Items.Count(item => item.IsDisabled));
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } topLevel && Config != null)
            await topLevel.Launcher.LaunchDirectoryInfoAsync(
                new DirectoryInfo(BedrockModManager.GetModsFolder(Config)));
    }

    private async void Import_OnClick(object? sender, RoutedEventArgs e)
    {
        await ImportAsync(null);
    }

    private void Resource_OnDragOver(object? sender, DragEventArgs e)
    {
        if (JavaResourceImport.Accepts(e.DataTransfer, ".dll"))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void Resource_OnDrop(object? sender, DragEventArgs e)
    {
        await ImportAsync(e);
    }

    private async Task ImportAsync(DragEventArgs? drop)
    {
        if (Config == null) return;
        var folder = BedrockModManager.GetModsFolder(Config);
        if (drop == null)
            await JavaResourceImport.SelectAndImportAsync(this,
                CommonLanguageManager.Instance.bedrockMods_selectDllMod.CurrentValue(), folder,
                CommonLanguageManager.Instance.bedrockMods_dllMod.CurrentValue(), [".dll"], false, RefreshAsync);
        else
            await JavaResourceImport.ImportDropAsync(this, drop, folder,
                CommonLanguageManager.Instance.bedrockMods_dllMod.CurrentValue(), [".dll"], false, RefreshAsync);
    }

    private void Title_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = RefreshAsync();
    }

    private void SearchBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        _filter = (sender as TextBox)?.Text ?? string.Empty;
        ApplyFilter();
    }

    private void ModCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Control).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed &&
            (sender as Control)?.DataContext is BedrockModItem item)
        {
            item.IsSelected = !item.IsSelected;
            RaiseSelection();
        }
    }

    private void SelectAll_OnClick(object? sender, RoutedEventArgs e)
    {
        SetSelection(_ => true);
    }

    private void ClearSelection_OnClick(object? sender, RoutedEventArgs e)
    {
        SetSelection(_ => false);
    }

    private void InvertSelection_OnClick(object? sender, RoutedEventArgs e)
    {
        SetSelection(item => !item.IsSelected);
    }

    private async void EnableSelected_OnClick(object? sender, RoutedEventArgs e)
    {
        await SetEnabledAsync(GetSelected(), true);
    }

    private async void DisableSelected_OnClick(object? sender, RoutedEventArgs e)
    {
        await SetEnabledAsync(GetSelected(), false);
    }

    private async void EnableMod_OnClick(object? sender, RoutedEventArgs e)
    {
        await SetEnabledAsync(Item(sender) is { } item ? [item] : [], true);
    }

    private async void DisableMod_OnClick(object? sender, RoutedEventArgs e)
    {
        await SetEnabledAsync(Item(sender) is { } item ? [item] : [], false);
    }

    private async Task SetEnabledAsync(IEnumerable<BedrockModItem> items, bool enabled)
    {
        if (Config == null) return;
        foreach (var item in items) BedrockModManager.Update(Config, item.FileName, entry => entry.Enabled = enabled);
        await RefreshAsync();
        ShowNotice(enabled
                ? CommonLanguageManager.Instance.bedrockMods_enabledSelected.CurrentValue()
                : CommonLanguageManager.Instance.bedrockMods_disabledSelected.CurrentValue(),
            NotificationType.Success);
    }

    private async void ShowDetails_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Item(sender) is not { } item || Config == null) return;
        var preload = new CheckBox
        {
            Content = CommonLanguageManager.Instance.bedrockMods_preloadLabel.CurrentValue(),
            IsChecked = item.Info.Config.Preload
        };
        var delay = new NumericUpDown
        {
            Minimum = 0, Maximum = BedrockModManager.MaximumDelayMs, Value = item.Info.Config.DelayMs,
            IsEnabled = !item.Info.Config.Preload, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        preload.IsCheckedChanged += (_, _) => delay.IsEnabled = preload.IsChecked != true;
        var panel = new StackPanel
        {
            Margin = new Thickness(10), Spacing = 8, MinWidth = 360,
            Children =
            {
                new TextBlock
                {
                    Text = string.Format(CommonLanguageManager.Instance.bedrockMods_fileInfo.CurrentValue(),
                        item.FileName, item.FileSizeText),
                    TextWrapping = TextWrapping.Wrap
                },
                preload,
                new TextBlock { Text = CommonLanguageManager.Instance.bedrockMods_delayMsLabel.CurrentValue() }, delay,
                new TextBlock
                {
                    Text = CommonLanguageManager.Instance.bedrockMods_preloadNoDelay.CurrentValue(),
                    Foreground = Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
        var result = await OverlayDialog.ShowStandardAsync(panel, null, this.TryGetHostId(),
            new OverlayDialogOptions
            {
                Title = CommonLanguageManager.Instance.bedrockMods_modDetailsTitle.CurrentValue(),
                Buttons = DialogButton.YesNo, OverrideYesButtonText = CommonLanguageManager.Instance.common_save.CurrentValue(),
                OverrideNoButtonText = CommonLanguageManager.Instance.common_cancel.CurrentValue(),
                CanResize = false
            });
        if (result != DialogResult.Yes) return;
        BedrockModManager.Update(Config, item.FileName, entry =>
        {
            entry.Preload = preload.IsChecked == true;
            entry.DelayMs = (int)(delay.Value ?? BedrockModManager.DefaultDelayMs);
        });
        await RefreshAsync();
        ShowNotice(CommonLanguageManager.Instance.bedrockMods_settingsSaved.CurrentValue(), NotificationType.Success);
    }

    private async void DeleteSelected_OnClick(object? sender, RoutedEventArgs e)
    {
        await ConfirmDeleteAsync(GetSelected());
    }

    private async void DeleteMod_OnClick(object? sender, RoutedEventArgs e)
    {
        await ConfirmDeleteAsync(Item(sender) is { } item ? [item] : []);
    }

    private async Task ConfirmDeleteAsync(BedrockModItem[] items)
    {
        if (items.Length == 0) return;
        var result = await OverlayDialog.ShowStandardAsync(
            new TextBlock
            {
                Margin = new Thickness(24),
                Text = string.Format(CommonLanguageManager.Instance.bedrockMods_deleteConfirm.CurrentValue(),
                    items.Length),
                TextWrapping = TextWrapping.Wrap
            }, null, this.TryGetHostId(),
            new OverlayDialogOptions
            {
                Title = CommonLanguageManager.Instance.resourceList_deleteModsTitle.CurrentValue(),
                Mode = DialogMode.Error, Buttons = DialogButton.YesNo,
                OverrideYesButtonText = CommonLanguageManager.Instance.dashboard_delete.CurrentValue(),
                OverrideNoButtonText = CommonLanguageManager.Instance.common_cancel.CurrentValue(),
                CanLightDismiss = false, CanResize = false
            });
        if (result != DialogResult.Yes) return;
        var failed = 0;
        foreach (var item in items)
            try
            {
                File.Delete(item.Info.FilePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failed++;
            }

        await RefreshAsync();
        ShowNotice(failed == 0
                ? CommonLanguageManager.Instance.bedrockMods_deletedSelected.CurrentValue()
                : string.Format(CommonLanguageManager.Instance.bedrockMods_deleteFailed.CurrentValue(), failed),
            failed == 0 ? NotificationType.Success : NotificationType.Warning);
    }

    private async void OpenModFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Item(sender) is not { } item || TopLevel.GetTopLevel(this) is not { } topLevel) return;
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Info.FilePath}\"")
                { UseShellExecute = true });
            return;
        }

        await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(Path.GetDirectoryName(item.Info.FilePath)!));
    }

    private BedrockModItem[] GetSelected()
    {
        return Items.Where(item => item.IsSelected).ToArray();
    }

    private static BedrockModItem? Item(object? sender)
    {
        return (sender as Control)?.Tag as BedrockModItem;
    }

    private void SetSelection(Func<BedrockModItem, bool> select)
    {
        foreach (var item in Items) item.IsSelected = select(item);
        RaiseSelection();
    }

    private void RaiseList()
    {
        Raise(nameof(IsEmpty));
        Raise(nameof(CountText));
    }

    private void RaiseSelection()
    {
        Raise(nameof(SelectedCount));
        Raise(nameof(SelectedCountText));
        Raise(nameof(HasSelection));
    }

    private void ShowNotice(string message, NotificationType type)
    {
        if (TopLevel.GetTopLevel(this) is { } topLevel) topLevel.Notice(message, type);
    }

    private void Raise(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class BedrockModItem(BedrockModInfo info) : INotifyPropertyChanged
{
    private bool _isSelected;
    public BedrockModInfo Info { get; } = info;
    public string FileName => Info.FileName;
    public string FileSizeText => ResourceListUi.FormatSize(Info.FileSize);
    public string SizeAndNameText => $"{FileSizeText}·{FileName}";
    public DateTime LastWriteTime => ReadLastWriteTime(Info.FilePath);
    public string LastWriteTimeText =>
        string.Format(CommonLanguageManager.Instance.bedrockMods_addedAt.CurrentValue(), LastWriteTime);
    public bool IsEnabled => Info.Config.Enabled;
    public bool IsDisabled => !IsEnabled;
    public bool IsPreload => Info.Config.Preload;
    public bool IsDelayed => IsEnabled && !IsPreload;
    public string DelayText => $"{Info.Config.DelayMs} ms";

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

    public event PropertyChangedEventHandler? PropertyChanged;

    private static DateTime ReadLastWriteTime(string path)
    {
        try
        {
            return new FileInfo(path).LastWriteTime;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }
}
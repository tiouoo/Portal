using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Models;
using Portal.Core.Minecraft.Services;
using Portal.Core.Const;
using Portal.Localization;
using Portal.Views.Pages.DownloadPages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;
using AutoCompleteBox = Avalonia.Controls.AutoCompleteBox;

namespace Portal.Views.Pages.InstancePages;

public partial class ShaderPacks : UserControl, INotifyPropertyChanged
{
    private readonly MinecraftInstance? _instance;
    private readonly ResourceUpdateService _updateService = new();
    private FilterSortMenuController? _filterSortMenu;
    private string _filter = string.Empty;
    private ResourceFilterMode _filterMode = ResourceFilterMode.All;
    private bool _hasLoaded;
    private bool _isLoading;
    private bool _updateCheckRunning;
    private ResourceSortMode _sortMode = ResourceSortMode.FileName;

    private static readonly string[] FilterBaseNames =
    [
        CommonLanguageManager.Instance.mod_all.CurrentValue(),
        CommonLanguageManager.Instance.resourceList_canUpdate.CurrentValue()
    ];

    public ShaderPacks()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, Resource_OnDragOver);
        AddHandler(DragDrop.DropEvent, Resource_OnDrop);
        SelectAllCommand = new RelayCommand(() => SetSelection(item => true));
        ClearSelectionCommand = new RelayCommand(() => SetSelection(item => false));
        InvertSelectionCommand = new RelayCommand(() => SetSelection(item => !item.IsSelected));
        DataContext = this;
        FilterOptions.Add(new ResourceFilterOption(ResourceListUi.BuildFilterLabel(
            CommonLanguageManager.Instance.mod_all.CurrentValue(), 0)));
        FilterOptions.Add(new ResourceFilterOption(ResourceListUi.BuildFilterLabel(
            CommonLanguageManager.Instance.resourceList_canUpdate.CurrentValue(), 0)));
        _filterSortMenu = new FilterSortMenuController(FilterSortButton,
            CommonLanguageManager.Instance.resourceList_sortBy.CurrentValue(),
            CommonLanguageManager.Instance.resourceList_filter.CurrentValue(), FilterOptions,
            FilterBaseNames, OnSortSelected, OnFilterSelected);

        KeyBindings.Add(new KeyBinding
        {
            Command = new RelayCommand(() => SetSelection(item => true), () => !IsTextInputFocused()),
            Gesture = KeyGesture.Parse("ctrl+A")
        });
        KeyBindings.Add(new KeyBinding { Command = ClearSelectionCommand, Gesture = KeyGesture.Parse("ctrl+Shift+A") });
        KeyBindings.Add(new KeyBinding { Command = InvertSelectionCommand, Gesture = KeyGesture.Parse("ctrl+I") });
    }

    public ShaderPacks(MinecraftInstance instance) : this()
    {
        _instance = instance;
    }

    public ObservableCollection<ShaderPackItem> Items { get; } = [];
    public ObservableCollection<ShaderPackItem> FilteredItems { get; } = [];
    public string[] SortOptions => ResourceListUi.SortOptions;
    public ObservableCollection<ResourceFilterOption> FilterOptions { get; } = [];

    public IRelayCommand SelectAllCommand { get; }
    public IRelayCommand ClearSelectionCommand { get; }
    public IRelayCommand InvertSelectionCommand { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            RaisePropertyChanged(nameof(IsLoading));
        }
    }

    public bool IsEmpty => !IsLoading && FilteredItems.Count == 0;
    public string ShaderPackCountText => IsLoading
        ? string.Empty
        : string.Format(CommonLanguageManager.Instance.resourceList_count.CurrentValue(), FilteredItems.Count);
    public int SelectedCount => Items.Count(item => item.IsSelected);
    public string SelectedCountText =>
        string.Format(CommonLanguageManager.Instance.resourceList_batchSelected.CurrentValue(), SelectedCount);
    public bool HasMultipleSelection => SelectedCount >= 1;

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnsureDefaultSelections();
        _ = LoadAsync();
    }

    private void EnsureDefaultSelections()
    {
        _filterSortMenu?.SetSortIndex(Math.Clamp(Data.ConfigEntry.ResourceListSortIndex, 0,
            ResourceListUi.SortOptions.Length - 1));
        _filterSortMenu?.SetFilterIndex(0);
    }

    private void OnSortSelected(int index)
    {
        Data.ConfigEntry.ResourceListSortIndex = index;
        _sortMode = index switch
        {
            1 => ResourceSortMode.Name,
            2 => ResourceSortMode.LastWriteTime,
            3 => ResourceSortMode.FileSize,
            _ => ResourceSortMode.FileName
        };
        ApplyFilter();
    }

    private void OnFilterSelected(int index)
    {
        _filterMode = index switch
        {
            1 => ResourceFilterMode.CanUpdate,
            _ => ResourceFilterMode.All
        };
        ApplyFilter();
    }

    private async Task LoadAsync()
    {
        if (_hasLoaded || _instance == null) return;

        _hasLoaded = true;
        IsLoading = true;
        RaiseListProperties();
        var folder = _instance.GetSpecialFolder(MinecraftSpecialFolder.ShaderPacksFolder);
        var files = await Task.Run(() => Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder).Where(IsShaderPackFile)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()
            : []);
        Items.Clear();
        RaiseSelectionProperties();
        foreach (var file in files)
            Items.Add(new ShaderPackItem(file));
        ApplyFilter();
        IsLoading = false;
        RaiseListProperties();
        _ = CheckUpdatesAsync();
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
        _filterSortMenu?.SyncFilterLabels(FilterOptions);
        RaiseListProperties();
    }

    private bool MatchesStateFilter(ShaderPackItem item)
    {
        return _filterMode switch
        {
            ResourceFilterMode.CanUpdate => item.HasUpdate,
            _ => true
        };
    }

    private IEnumerable<ShaderPackItem> SortItems(IEnumerable<ShaderPackItem> source)
    {
        return _sortMode switch
        {
            ResourceSortMode.Name => source.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase),
            ResourceSortMode.LastWriteTime => source.OrderByDescending(item => item.LastWriteTime),
            ResourceSortMode.FileSize => source.OrderByDescending(item => item.FileSize),
            _ => source.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
        };
    }

    private void RefreshFilterOptions()
    {
        while (FilterOptions.Count < 2)
            FilterOptions.Add(new ResourceFilterOption(""));
        FilterOptions[0].Label = ResourceListUi.BuildFilterLabel(CommonLanguageManager.Instance.mod_all.CurrentValue(),
            Items.Count);
        FilterOptions[1].Label = ResourceListUi.BuildFilterLabel(
            CommonLanguageManager.Instance.resourceList_canUpdate.CurrentValue(), Items.Count(item => item.HasUpdate));
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel || _instance == null) return;
        await topLevel.Launcher.LaunchDirectoryInfoAsync(
            new DirectoryInfo(_instance.GetSpecialFolder(MinecraftSpecialFolder.ShaderPacksFolder)));
    }

    private void CheckUpdates_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = CheckUpdatesAsync(true);
    }

    private async void Import_OnClick(object? sender, RoutedEventArgs e)
    {
        await ImportAsync(null);
    }

    private void Resource_OnDragOver(object? sender, DragEventArgs e)
    {
        if (JavaResourceImport.Accepts(e.DataTransfer, ".zip"))
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
        if (_instance == null) return;
        var refresh = async () =>
        {
            _hasLoaded = false;
            await LoadAsync();
        };
        var destination = _instance.GetSpecialFolder(MinecraftSpecialFolder.ShaderPacksFolder);
        var packName = CommonLanguageManager.Instance.resourceList_packNameShaderPack.CurrentValue();
        if (drop == null)
            await JavaResourceImport.SelectAndImportAsync(this,
                string.Format(CommonLanguageManager.Instance.resourceList_selectPack.CurrentValue(), packName),
                destination, packName, [".zip"], false, refresh);
        else await JavaResourceImport.ImportDropAsync(this, drop, destination, packName, [".zip"], false, refresh);
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

    private void ShaderPackCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Control).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed ||
            (sender as Control)?.DataContext is not ShaderPackItem item)
            return;

        item.IsSelected = !item.IsSelected;
        RaiseSelectionProperties();
    }

    private bool IsTextInputFocused()
    {
        return TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox
            or AutoCompleteBox or TioUi.Controls.AutoCompleteBox;
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
        var selected = GetSelectedItems();
        if (selected.Length < 1) return;

        var result = await OverlayDialog.ShowStandardAsync(new TextBlock
        {
            Margin = new Thickness(24),
            Text = string.Format(CommonLanguageManager.Instance.resourceList_deleteSelectedConfirmPack.CurrentValue(),
                selected.Length,
                CommonLanguageManager.Instance.resourceList_packNameShaderPack.CurrentValue()),
            TextWrapping = TextWrapping.Wrap
        }, null, this.TryGetHostId(), CreateDeleteConfirmationOptions());
        if (result == DialogResult.Yes)
            await RunSelectedFileActionAsync(selected, item => File.Delete(item.FilePath),
                CommonLanguageManager.Instance.dashboard_delete.CurrentValue());
    }

    private void ShowShaderPackDetails_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetShaderPackItem(sender) is not { } item || TopLevel.GetTopLevel(this) is not { } topLevel) return;
        var result = item.UpdateResult;
        if (result?.HasIdentity != true || result.Source is not { } source ||
            string.IsNullOrEmpty(result.ProjectId)) return;
        ResourceDetailsPage.Open(topLevel, new ResourceDetailsTarget(ResourceDefinitions.Mod, source, result.ProjectId), item.FileName);
    }

    private async void DeleteShaderPack_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetShaderPackItem(sender) is not { } item) return;

        var result = await OverlayDialog.ShowStandardAsync(new TextBlock
        {
            Margin = new Thickness(24),
            Text = string.Format(CommonLanguageManager.Instance.resourceList_deleteConfirmPack.CurrentValue(),
                CommonLanguageManager.Instance.resourceList_packNameShaderPack.CurrentValue(), item.FileName),
            TextWrapping = TextWrapping.Wrap
        }, null, this.TryGetHostId(), CreateDeleteConfirmationOptions());
        if (result == DialogResult.Yes)
            await RunSelectedFileActionAsync([item], shaderPack => File.Delete(shaderPack.FilePath),
                CommonLanguageManager.Instance.dashboard_delete.CurrentValue());
    }

    private async void OpenShaderPackFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetShaderPackItem(sender) is not { } item || TopLevel.GetTopLevel(this) is not { } topLevel) return;
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FilePath}\"")
                { UseShellExecute = true });
            return;
        }

        await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(Path.GetDirectoryName(item.FilePath)!));
    }

    private async Task RunSelectedFileActionAsync(IEnumerable<ShaderPackItem> selected, Action<ShaderPackItem> action,
        string actionName)
    {
        var failed = 0;
        foreach (var item in selected)
            try
            {
                action(item);
            }
            catch (IOException)
            {
                failed++;
            }
            catch (UnauthorizedAccessException)
            {
                failed++;
            }

        _hasLoaded = false;
        await LoadAsync();
        ShowNotice(failed == 0
                ? string.Format(CommonLanguageManager.Instance.resourceList_actionCompletedShader.CurrentValue(),
                    actionName)
                : string.Format(CommonLanguageManager.Instance.resourceList_actionFailedWithCountShader.CurrentValue(),
                    actionName, failed),
            failed == 0 ? NotificationType.Success : NotificationType.Warning);
    }

    private ShaderPackItem[] GetSelectedItems()
    {
        return Items.Where(item => item.IsSelected).ToArray();
    }

    private async Task CheckUpdatesAsync(bool forceRefresh = false)
    {
        if (_updateCheckRunning || _instance == null) return;
        _updateCheckRunning = true;
        try
        {
            var candidates = Items.Select(item => new ResourceUpdateCandidate(item.FilePath, ResourceKind.ShaderPack))
                .ToArray();
            var results = await _updateService.CheckUpdatesAsync(_instance, candidates, forceRefresh);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var item in Items)
                    if (results.TryGetValue(item.FilePath, out var result))
                        item.SetUpdateResult(result);
                RefreshRollbackStates();
                ApplyFilter();
            }, DispatcherPriority.Background);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[ShaderPacks] Update check failed: {exception}");
        }
        finally
        {
            _updateCheckRunning = false;
        }
    }

    private void RefreshRollbackStates()
    {
        if (_instance == null) return;
        var folder = _instance.GetSpecialFolder(MinecraftSpecialFolder.ShaderPacksFolder);
        var targets = ResourceBackupStore.FindRollbackTargets(folder, Items.Select(item => item.FilePath));
        foreach (var item in Items)
            item.SetRollback(targets.Contains(item.FilePath));
    }

    private async void UpdateShaderPack_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetShaderPackItem(sender) is not { } item || item.UpdateFile is not { } file)
            return;
        await UpdateToVersionAsync(item, file);
    }

    private async void SwitchVersionShaderPack_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetShaderPackItem(sender) is not { } item || TopLevel.GetTopLevel(this) is not { } topLevel ||
            _instance == null)
            return;

        var result = item.UpdateResult;
        if (result?.HasIdentity != true || result.Source is not { } source)
        {
            ShowNotice(CommonLanguageManager.Instance.resourceList_platformUnknownSwitchResource.CurrentValue(),
                NotificationType.Warning);
            return;
        }

        var file = await ResourceVersionSwitchDialog.ShowAsync(topLevel,
            new ResourceVersionSwitchTarget(source, result.ProjectId!, result.CurrentVersionId ?? string.Empty,
                ResourceKind.ShaderPack, _instance));
        if (file is null)
            return;
        await UpdateToVersionAsync(item, file);
    }

    private async void RollbackShaderPack_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetShaderPackItem(sender) is not { } item)
            return;
        await RollbackAsync(item);
    }

    private async Task UpdateToVersionAsync(ShaderPackItem item, ResourceVersionFileItem file)
    {
        if (_instance == null || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;
        if (item.IsUpdating) return;
        item.SetIsUpdating(true);
        var destination = _instance.GetSpecialFolder(MinecraftSpecialFolder.ShaderPacksFolder);
        try
        {
            var tempPath = Path.Combine(destination, $".portal-update-{Guid.NewGuid():N}.zip");
            var packName = CommonLanguageManager.Instance.resourceList_packNameShaderPack.CurrentValue();
            var task = DownloadTasks.Download(topLevel,
                string.Format(CommonLanguageManager.Instance.resourceList_updatePack.CurrentValue(), packName,
                    file.DisplayName),
                CommonLanguageManager.Instance.resourceList_cancelUpdate.CurrentValue(),
                file.FileName, file.DownloadUrl, tempPath, file.FileSize,
                afterDownload: _ =>
                {
                    var oldPath = item.FilePath;
                    var newPath = ResourceUpdateService.ApplyUpdateFile(oldPath, tempPath, file.FileName);
                    ResourceUpdateService.InvalidateCache(oldPath);
                    ResourceUpdateService.InvalidateCache(newPath);
                    return Task.CompletedTask;
                }, completedText: string.Format(
                    CommonLanguageManager.Instance.resourceList_packUpdated.CurrentValue(), packName));
            await task.Completion;
            await ReloadAsync();
            _ = CheckUpdatesAsync(true);
        }
        catch (OperationCanceledException)
        {
            ShowNotice(string.Format(CommonLanguageManager.Instance.resourceList_packUpdateCancelled.CurrentValue(),
                CommonLanguageManager.Instance.resourceList_packNameShaderPack.CurrentValue()),
                NotificationType.Warning);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[ShaderPacks] Update failed for {item.FilePath}: {exception}");
            ShowNotice(string.Format(CommonLanguageManager.Instance.resourceList_packUpdateFailed.CurrentValue(),
                CommonLanguageManager.Instance.resourceList_packNameShaderPack.CurrentValue()),
                NotificationType.Error);
        }
        finally
        {
            item.SetIsUpdating(false);
        }
    }

    private async Task RollbackAsync(ShaderPackItem item)
    {
        if (_instance == null || item.IsUpdating) return;
        item.SetIsUpdating(true);
        try
        {
            var newPath = await Task.Run(() =>
            {
                var path = ResourceBackupStore.Rollback(item.FilePath);
                if (path == null)
                    return null;
                ResourceUpdateService.InvalidateCache(item.FilePath);
                ResourceUpdateService.InvalidateCache(path);
                return path;
            });
            if (newPath == null)
            {
                ShowNotice(CommonLanguageManager.Instance.resourceList_noRollback.CurrentValue(),
                    NotificationType.Warning);
                return;
            }

            await ReloadAsync();
            _ = CheckUpdatesAsync(true);
            ShowNotice(string.Format(CommonLanguageManager.Instance.resourceList_packRolledBack.CurrentValue(),
                CommonLanguageManager.Instance.resourceList_packNameShaderPack.CurrentValue()),
                NotificationType.Success);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[ShaderPacks] Rollback failed for {item.FilePath}: {exception}");
            ShowNotice(string.Format(CommonLanguageManager.Instance.resourceList_packRollbackFailed.CurrentValue(),
                CommonLanguageManager.Instance.resourceList_packNameShaderPack.CurrentValue()),
                NotificationType.Error);
        }
        finally
        {
            item.SetIsUpdating(false);
        }
    }

    private async Task ReloadAsync()
    {
        _hasLoaded = false;
        await LoadAsync();
    }

    private static bool IsShaderPackFile(string path)
    {
        return path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".zip.disabled", StringComparison.OrdinalIgnoreCase);
    }

    private static ShaderPackItem? GetShaderPackItem(object? sender)
    {
        return (sender as Control)?.Tag as ShaderPackItem;
    }

    private static OverlayDialogOptions CreateDeleteConfirmationOptions()
    {
        return new OverlayDialogOptions
        {
            Title = string.Format(CommonLanguageManager.Instance.resourceList_deletePackTitle.CurrentValue(),
                CommonLanguageManager.Instance.resourceList_packNameShaderPack.CurrentValue()),
            Mode = DialogMode.Error, Buttons = DialogButton.YesNo,
            OverrideYesButtonText = CommonLanguageManager.Instance.dashboard_delete.CurrentValue(),
            OverrideNoButtonText = CommonLanguageManager.Instance.common_cancel.CurrentValue(),
            CanLightDismiss = false, CanResize = false
        };
    }

    private void SetSelection(Func<ShaderPackItem, bool> selection)
    {
        foreach (var item in Items)
            item.IsSelected = selection(item);
        RaiseSelectionProperties();
    }

    private void RaiseListProperties()
    {
        RaisePropertyChanged(nameof(IsEmpty));
        RaisePropertyChanged(nameof(ShaderPackCountText));
    }

    private void RaiseSelectionProperties()
    {
        RaisePropertyChanged(nameof(SelectedCount));
        RaisePropertyChanged(nameof(SelectedCountText));
        RaisePropertyChanged(nameof(HasMultipleSelection));
    }

    private void ShowNotice(string message, NotificationType type)
    {
        if (TopLevel.GetTopLevel(this) is { } topLevel)
            topLevel.Notice(message, type);
    }

    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ShaderPackItem(string filePath) : INotifyPropertyChanged
{
    private bool _hasRollback;
    private bool _isSelected;
    private bool _isUpdating;
    private ResourceUpdateResult? _updateResult;
    public string FilePath { get; } = filePath;
    public string FileName { get; } = Path.GetFileName(filePath);
    public long FileSize { get; } = ReadFileSize(filePath);
    public DateTime LastWriteTime { get; } = ReadLastWriteTime(filePath);
    public string SizeAndNameText => $"{ResourceListUi.FormatSize(FileSize)}·{FileName}";

    public ResourceUpdateResult? UpdateResult => _updateResult;
    public bool HasUpdate => _updateResult?.HasUpdate == true;
    public bool IsUpdatable => HasUpdate;
    public bool HasIdentity => _updateResult?.HasIdentity == true;
    public bool HasDetails => HasIdentity;
    public ResourceVersionFileItem? UpdateFile => _updateResult?.TargetFile;
    public bool HasRollback => _hasRollback;
    public bool IsUpdating => _isUpdating;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetUpdateResult(ResourceUpdateResult? result)
    {
        if (ReferenceEquals(_updateResult, result)) return;
        _updateResult = result;
        Raise(nameof(HasUpdate));
        Raise(nameof(IsUpdatable));
        Raise(nameof(HasIdentity));
        Raise(nameof(HasDetails));
        Raise(nameof(UpdateFile));
        Raise(nameof(UpdateResult));
    }

    public void SetRollback(bool hasRollback)
    {
        if (_hasRollback == hasRollback) return;
        _hasRollback = hasRollback;
        Raise(nameof(HasRollback));
    }

    public void SetIsUpdating(bool isUpdating)
    {
        if (_isUpdating == isUpdating) return;
        _isUpdating = isUpdating;
        Raise(nameof(IsUpdating));
    }

    private void Raise(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static long ReadFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

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
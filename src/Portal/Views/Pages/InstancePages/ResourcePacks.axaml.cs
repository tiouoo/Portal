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
using Avalonia.Threading;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Models;
using Portal.Core.Minecraft.Services;
using Portal.Views.Pages.DownloadPages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class ResourcePacks : UserControl, INotifyPropertyChanged, IDisposable
{
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly MinecraftSpecialFolder _folder = MinecraftSpecialFolder.ResourcePacksFolder;
    private readonly MinecraftInstance? _instance;
    private readonly bool _isCompactLayout;
    private readonly ResourcePackService _resourcePackService = new();
    private readonly ResourceUpdateService _updateService = new();
    private string _filter = string.Empty;
    private ResourceFilterMode _filterMode = ResourceFilterMode.All;
    private bool _hasLoaded;
    private bool _isDisposed;
    private bool _isLoading;
    private bool _updateCheckRunning;
    private ResourceSortMode _sortMode = ResourceSortMode.FileName;

    public ResourcePacks()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, Resource_OnDragOver);
        AddHandler(DragDrop.DropEvent, Resource_OnDrop);
        DataContext = this;
        FilterOptions.Add(new ResourceFilterOption(ResourceListUi.BuildFilterLabel("全部", 0)));
        FilterOptions.Add(new ResourceFilterOption(ResourceListUi.BuildFilterLabel("可更新", 0)));
    }

    public ResourcePacks(MinecraftInstance instance) : this(instance, MinecraftSpecialFolder.ResourcePacksFolder, "资源包")
    {
    }

    protected ResourcePacks(MinecraftInstance instance, MinecraftSpecialFolder folder, string packName,
        bool isCompactLayout = false) : this()
    {
        _instance = instance;
        _folder = folder;
        PackName = packName;
        _isCompactLayout = isCompactLayout;
        RaisePropertyChanged(nameof(PackName));
        RaisePropertyChanged(nameof(SearchPlaceholder));
        RaisePropertyChanged(nameof(LoadingText));
        RaisePropertyChanged(nameof(EmptyText));
    }

    public ObservableCollection<ResourcePackItem> Items { get; } = [];
    public ObservableCollection<ResourcePackItem> FilteredItems { get; } = [];
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
                RaisePropertyChanged(nameof(IsLoading));
            }
        }
    }

    public bool IsEmpty => !IsLoading && FilteredItems.Count == 0;
    public string ResourcePackCountText => IsLoading ? string.Empty : $"{FilteredItems.Count} 个";
    public int SelectedCount => Items.Count(item => item.IsSelected);
    public string SelectedCountText => $"批量操作{SelectedCount}个";
    public bool HasMultipleSelection => SelectedCount >= 1;
    public string PackName { get; } = "资源包";

    public string SearchPlaceholder => $"搜索{PackName}";
    public string LoadingText => $"正在读取{PackName}...";
    public string EmptyText => $"此实例没有可识别的{PackName}";
    public int CardMinHeight => 117;

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
        EnsureDefaultSelections();
        _hasLoaded = true;
        IsLoading = true;
        RaiseListProperties();
        try
        {
            var packs = await _resourcePackService.ScanAsync(_instance, _folder, _disposeCancellation.Token);
            if (_isDisposed) return;
            foreach (var item in Items) item.Dispose();
            Items.Clear();
            RaiseSelectionProperties();
            foreach (var pack in packs) Items.Add(new ResourcePackItem(pack, _isCompactLayout));
            ApplyFilter();
            _ = CheckUpdatesAsync();
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[ResourcePacks] {PackName} scan cancelled: {exception}");
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
        if (TopLevel.GetTopLevel(this) is not { } topLevel || _instance == null)
            return;

        await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(_instance.GetSpecialFolder(_folder)));
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
        var extensions = _instance?.IsBedrock == true ? new[] { ".mcpack", ".mcaddon" } : new[] { ".zip" };
        if (_instance?.IsBedrock == true
                ? BedrockResourceImport.Accepts(e.DataTransfer, extensions)
                : JavaResourceImport.Accepts(e.DataTransfer, extensions))
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
        if (_instance.IsBedrock)
        {
            var expectedType = _folder == MinecraftSpecialFolder.BehaviorPacksFolder
                ? BedrockPackageContentType.BehaviorPack
                : _folder == MinecraftSpecialFolder.SkinPacksFolder
                    ? BedrockPackageContentType.SkinPack
                    : BedrockPackageContentType.ResourcePack;
            if (drop == null)
                await BedrockResourceImport.SelectAndImportAsync(this, _instance, $"选择{PackName}", PackName,
                    [".mcpack", ".mcaddon"], null, expectedType, refresh);
            else
                await BedrockResourceImport.ImportDropAsync(this, drop, _instance, PackName, [".mcpack", ".mcaddon"],
                    null, expectedType, refresh);
            return;
        }

        var destination = _instance.GetSpecialFolder(_folder);
        if (drop == null)
            await JavaResourceImport.SelectAndImportAsync(this, $"选择{PackName}", destination, PackName, [".zip"], false,
                refresh);
        else await JavaResourceImport.ImportDropAsync(this, drop, destination, PackName, [".zip"], false, refresh);
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
        var query = string.IsNullOrWhiteSpace(_filter)
            ? Items
            : Items.Where(item =>
                item.DisplayName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                item.FileName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                item.DescriptionText.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        FilteredItems.Clear();
        foreach (var item in SortItems(query).Where(MatchesStateFilter))
            FilteredItems.Add(item);
        while (FilterOptions.Count < 2)
            FilterOptions.Add(new ResourceFilterOption(""));
        FilterOptions[0].Label = ResourceListUi.BuildFilterLabel("全部", Items.Count);
        FilterOptions[1].Label = ResourceListUi.BuildFilterLabel("可更新", Items.Count(item => item.HasUpdate));
        RaiseListProperties();
    }

    private bool MatchesStateFilter(ResourcePackItem item)
    {
        return _filterMode switch
        {
            ResourceFilterMode.CanUpdate => item.HasUpdate,
            _ => true
        };
    }

    private void EnsureDefaultSelections()
    {
        if (FilterComboBox.SelectedIndex < 0)
            FilterComboBox.SelectedIndex = 0;
        if (SortComboBox.SelectedIndex < 0)
            SortComboBox.SelectedIndex = 0;
    }

    private void SortComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedIndex: >= 0 } combo)
            return;
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
            1 => ResourceFilterMode.CanUpdate,
            _ => ResourceFilterMode.All
        };
        ApplyFilter();
    }

    private IEnumerable<ResourcePackItem> SortItems(IEnumerable<ResourcePackItem> source)
    {
        return _sortMode switch
        {
            ResourceSortMode.Name => source.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase),
            ResourceSortMode.LastWriteTime => source.OrderByDescending(item => item.Info.LastWriteTime),
            ResourceSortMode.FileSize => source.OrderByDescending(item => item.Info.FileSize),
            _ => source.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
        };
    }

    private void ResourcePackCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Control).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed ||
            (sender as Control)?.DataContext is not ResourcePackItem item) return;
        item.IsSelected = !item.IsSelected;
        RaiseSelectionProperties();
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
        if (selected.Length < 2) return;
        if (await ConfirmDeleteAsync($"确定要永久删除选中的 {selected.Length} 个{PackName}吗？此操作无法撤销。") == DialogResult.Yes)
            await DeleteAsync(selected);
    }

    private async void DeleteResourcePack_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item) return;
        if (await ConfirmDeleteAsync($"确定要永久删除{PackName}“{item.DisplayName}”吗？此操作无法撤销。") == DialogResult.Yes)
            await DeleteAsync([item]);
    }

    private async void ShowResourcePackDetails_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item) return;
        if (item.Info.IsBedrock)
        {
            await OverlayDialog.ShowStandardAsync(new TextBlock
                {
                    Margin = new Thickness(24),
                    Text = item.DetailsText,
                    TextWrapping = TextWrapping.Wrap
                }, null, this.TryGetHostId(),
                new OverlayDialogOptions { Title = $"{PackName}详情", Buttons = DialogButton.OK, CanResize = false });
            return;
        }

        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;
        var result = item.UpdateResult;
        if (result?.HasIdentity != true || result.Source is not { } source ||
            string.IsNullOrEmpty(result.ProjectId)) return;
        ModDetailsPage.Open(topLevel, new ModDetailsTarget(source, result.ProjectId), item.DisplayName);
    }

    private async void OpenResourcePackFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item || TopLevel.GetTopLevel(this) is not { } topLevel) return;
        if (item.Info.IsBedrock)
        {
            await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(item.Info.FilePath));
            return;
        }

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
        return await OverlayDialog.ShowStandardAsync(new TextBlock
        {
            Margin = new Thickness(24), Text = message, TextWrapping = TextWrapping.Wrap
        }, null, this.TryGetHostId(), new OverlayDialogOptions
        {
            Title = $"删除{PackName}", Mode = DialogMode.Error,
            Buttons = DialogButton.YesNo, OverrideYesButtonText = "删除", OverrideNoButtonText = "取消",
            CanLightDismiss = false, CanResize = false
        });
    }

    private async Task DeleteAsync(IEnumerable<ResourcePackItem> items)
    {
        var failed = 0;
        foreach (var item in items)
            try
            {
                if (item.Info.IsBedrock) Directory.Delete(item.Info.FilePath, true);
                else File.Delete(item.Info.FilePath);
            }
            catch (IOException exception)
            {
                Logger.Warning($"[ResourcePacks] Failed to delete {item.Info.FilePath}: {exception}");
                failed++;
            }
            catch (UnauthorizedAccessException exception)
            {
                Logger.Warning($"[ResourcePacks] Failed to delete {item.Info.FilePath}: {exception}");
                failed++;
            }

        _hasLoaded = false;
        await LoadAsync();
        if (TopLevel.GetTopLevel(this) is { } topLevel)
            topLevel.Notice(failed == 0 ? $"已删除所选{PackName}" : $"删除完成，但有 {failed} 个{PackName}操作失败",
                failed == 0 ? NotificationType.Success : NotificationType.Warning);
    }

    private async Task CheckUpdatesAsync(bool forceRefresh = false)
    {
        if (_updateCheckRunning || _instance == null || _isDisposed) return;
        _updateCheckRunning = true;
        try
        {
            var kind = _folder == MinecraftSpecialFolder.ShaderPacksFolder
                ? ResourceKind.ShaderPack
                : _folder == MinecraftSpecialFolder.ResourcePacksFolder
                    ? ResourceKind.ResourcePack
                    : ResourceKind.ResourcePack;
            var candidates = Items.Select(item => new ResourceUpdateCandidate(item.Info.FilePath, kind)).ToArray();
            var results = await _updateService.CheckUpdatesAsync(_instance, candidates, forceRefresh,
                _disposeCancellation.Token);
            if (_isDisposed) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_isDisposed) return;
                foreach (var item in Items)
                    if (results.TryGetValue(item.Info.FilePath, out var result))
                        item.SetUpdateResult(result);
                RefreshRollbackStates();
                ApplyFilter();
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[ResourcePacks] Update check cancelled: {exception}");
        }
        catch (Exception exception)
        {
            Logger.Warning($"[ResourcePacks] Update check failed: {exception}");
        }
        finally
        {
            _updateCheckRunning = false;
        }
    }

    private void RefreshRollbackStates()
    {
        if (_instance == null) return;
        var folder = _instance.GetSpecialFolder(_folder);
        var targets = ResourceBackupStore.FindRollbackTargets(folder, Items.Select(item => item.Info.FilePath));
        foreach (var item in Items)
            item.SetRollback(targets.Contains(item.Info.FilePath));
    }

    private async void UpdateResourcePack_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item || item.UpdateFile is not { } file)
            return;
        await UpdateToVersionAsync(item, file);
    }

    private async void SwitchVersionResourcePack_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item || TopLevel.GetTopLevel(this) is not { } topLevel || _instance == null)
            return;

        var result = item.UpdateResult;
        if (result?.HasIdentity != true || result.Source is not { } source)
        {
            ShowNotice("尚未识别此资源的平台信息，暂时无法切换版本", NotificationType.Warning);
            return;
        }

        var kind = _folder == MinecraftSpecialFolder.ShaderPacksFolder
            ? ResourceKind.ShaderPack
            : ResourceKind.ResourcePack;
        var file = await ResourceVersionSwitchDialog.ShowAsync(topLevel,
            new ResourceVersionSwitchTarget(source, result.ProjectId!, result.CurrentVersionId ?? string.Empty,
                kind, _instance));
        if (file is null)
            return;
        await UpdateToVersionAsync(item, file);
    }

    private async void RollbackResourcePack_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item)
            return;
        await RollbackAsync(item);
    }

    private async Task UpdateToVersionAsync(ResourcePackItem item, ModVersionFileItem file)
    {
        if (_instance == null || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;
        if (item.IsUpdating) return;
        item.SetIsUpdating(true);
        var destination = _instance.GetSpecialFolder(_folder);
        try
        {
            var tempPath = Path.Combine(destination, $".portal-update-{Guid.NewGuid():N}.zip");
            var task = DownloadTasks.Download(topLevel, $"更新{PackName}：{file.DisplayName}", "取消此更新",
                file.FileName, file.DownloadUrl, tempPath, file.FileSize,
                afterDownload: _ =>
                {
                    var oldPath = item.Info.FilePath;
                    var newPath = ResourceUpdateService.ApplyUpdateFile(oldPath, tempPath, file.FileName);
                    ResourceUpdateService.InvalidateCache(oldPath);
                    ResourceUpdateService.InvalidateCache(newPath);
                    return Task.CompletedTask;
                }, completedText: $"{PackName}已更新");
            await task.Completion;
            await ReloadAsync();
            _ = CheckUpdatesAsync(true);
        }
        catch (OperationCanceledException)
        {
            ShowNotice($"{PackName}更新已取消", NotificationType.Warning);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[ResourcePacks] Update failed for {item.Info.FilePath}: {exception}");
            ShowNotice($"{PackName}更新失败", NotificationType.Error);
        }
        finally
        {
            item.SetIsUpdating(false);
        }
    }

    private async Task RollbackAsync(ResourcePackItem item)
    {
        if (_instance == null || item.IsUpdating) return;
        item.SetIsUpdating(true);
        try
        {
            var newPath = await Task.Run(() =>
            {
                var path = ResourceBackupStore.Rollback(item.Info.FilePath);
                if (path == null)
                    return null;
                ResourceUpdateService.InvalidateCache(item.Info.FilePath);
                ResourceUpdateService.InvalidateCache(path);
                return path;
            });
            if (newPath == null)
            {
                ShowNotice("没有可回滚的版本", NotificationType.Warning);
                return;
            }

            await ReloadAsync();
            _ = CheckUpdatesAsync(true);
            ShowNotice($"{PackName}已回滚", NotificationType.Success);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[ResourcePacks] Rollback failed for {item.Info.FilePath}: {exception}");
            ShowNotice($"{PackName}回滚失败", NotificationType.Error);
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

    private static ResourcePackItem? GetItem(object? sender)
    {
        return (sender as Control)?.Tag as ResourcePackItem;
    }

    private void ShowNotice(string message, NotificationType type)
    {
        if (TopLevel.GetTopLevel(this) is { } topLevel)
            topLevel.Notice(message, type);
    }

    private void SetSelection(Func<ResourcePackItem, bool> selection)
    {
        foreach (var item in Items) item.IsSelected = selection(item);
        RaiseSelectionProperties();
    }

    private void RaiseListProperties()
    {
        RaisePropertyChanged(nameof(IsEmpty));
        RaisePropertyChanged(nameof(ResourcePackCountText));
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

public sealed class BehaviorPacks(MinecraftInstance instance) : ResourcePacks(instance,
    MinecraftSpecialFolder.BehaviorPacksFolder, "行为包");

public sealed class SkinPacks(MinecraftInstance instance) : ResourcePacks(instance,
    MinecraftSpecialFolder.SkinPacksFolder, "皮肤包", true);

public sealed class ResourcePackItem(ResourcePackInfo info, bool isCompactLayout = false)
    : INotifyPropertyChanged, IDisposable
{
    private bool _hasRollback;
    private bool _isSelected;
    private bool _isUpdating;
    private ResourceUpdateResult? _updateResult;
    public ResourcePackInfo Info { get; } = info;
    public string DisplayName => Info.DisplayName;
    public string FileName => Info.FileName;
    public string SizeAndNameText => $"{ResourceListUi.FormatSize(Info.FileSize)}·{FileName}";
    public bool IsCompactLayout { get; } = isCompactLayout;

    public ResourceUpdateResult? UpdateResult => _updateResult;
    public bool HasUpdate => _updateResult?.HasUpdate == true;
    public bool IsUpdatable => HasUpdate;
    public bool HasIdentity => _updateResult?.HasIdentity == true;
    public bool HasDetails => Info.IsBedrock || HasIdentity;
    public ModVersionFileItem? UpdateFile => _updateResult?.TargetFile;
    public bool HasRollback => _hasRollback;
    public bool IsUpdating => _isUpdating;

    public string SecondaryText => IsCompactLayout
        ? $"{ResourceListUi.FormatSize(Info.FileSize)}·{(Info.SkinCount is int count ? $"包含 {count} 个皮肤" : "皮肤数量未知")}"
        : Info.IsBedrock
            ? $"{ResourceListUi.FormatSize(Info.FileSize)}·最低支持版本：{Info.MinEngineVersion ?? "未知"}"
            : SizeAndNameText;

    public string DescriptionText => string.IsNullOrWhiteSpace(Info.Description) ? "没有可用的资源包描述" : Info.Description;
    public string SupportedFormatsText => Info.SupportedFormats ?? "未知";
    public string VersionLabel => Info.IsBedrock ? "版本:" : "支持格式:";

    public string DetailsText => IsCompactLayout
        ? $"名称：{DisplayName}\n文件夹：{FileName}\nUUID：{Info.Uuid?.ToLowerInvariant() ?? "未知"}\n版本：{SupportedFormatsText}\n皮肤：{DescriptionText}"
        : Info.IsBedrock
            ? $"名称：{DisplayName}\n文件夹：{FileName}\nUUID：{Info.Uuid?.ToLowerInvariant() ?? "未知"}\n版本：{SupportedFormatsText}\n最低引擎版本：{Info.MinEngineVersion ?? "未知"}\n作者：{(Info.Authors.Count == 0 ? "未知" : string.Join("、", Info.Authors))}\n模块：{(Info.Modules.Count == 0 ? "无" : string.Join("、", Info.Modules))}\n依赖：{(Info.Dependencies.Count == 0 ? "无" : string.Join("、", Info.Dependencies))}\n子包：{(Info.Subpacks.Count == 0 ? "无" : string.Join("、", Info.Subpacks))}\n能力：{(Info.Capabilities.Count == 0 ? "无" : string.Join("、", Info.Capabilities))}\n\n{DescriptionText}"
            : $"名称：{DisplayName}\n文件：{FileName}\n支持格式：{SupportedFormatsText}\n\n{DescriptionText}";

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
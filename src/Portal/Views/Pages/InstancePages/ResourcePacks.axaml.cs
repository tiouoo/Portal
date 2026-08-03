using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class ResourcePacks : UserControl, INotifyPropertyChanged, IDisposable
{
    private readonly MinecraftInstance? _instance;
    private MinecraftSpecialFolder _folder = MinecraftSpecialFolder.ResourcePacksFolder;
    private string _packName = "资源包";
    private bool _isCompactLayout;
    private readonly ResourcePackService _resourcePackService = new();
    private readonly CancellationTokenSource _disposeCancellation = new();
    private bool _hasLoaded;
    private bool _isLoading;
    private bool _isDisposed;
    private string _filter = string.Empty;

    public ObservableCollection<ResourcePackItem> Items { get; } = [];
    public ObservableCollection<ResourcePackItem> FilteredItems { get; } = [];
    public bool IsLoading { get => _isLoading; private set { if (_isLoading != value) { _isLoading = value; RaisePropertyChanged(nameof(IsLoading)); } } }
    public bool IsEmpty => !IsLoading && FilteredItems.Count == 0;
    public string ResourcePackCountText => IsLoading ? string.Empty : $"{FilteredItems.Count} 个";
    public int SelectedCount => Items.Count(item => item.IsSelected);
    public string SelectedCountText => $"批量操作{SelectedCount}个";
    public bool HasMultipleSelection => SelectedCount >= 1;
    public string PackName => _packName;
    public string SearchPlaceholder => $"搜索{PackName}";
    public string LoadingText => $"正在读取{PackName}...";
    public string EmptyText => $"此实例没有可识别的{PackName}";
    public int CardMinHeight => 117;

    public ResourcePacks()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, Resource_OnDragOver);
        AddHandler(DragDrop.DropEvent, Resource_OnDrop);
        DataContext = this;
    }

    public ResourcePacks(MinecraftInstance instance) : this(instance, MinecraftSpecialFolder.ResourcePacksFolder, "资源包") { }

    protected ResourcePacks(MinecraftInstance instance, MinecraftSpecialFolder folder, string packName, bool isCompactLayout = false) : this()
    {
        _instance = instance;
        _folder = folder;
        _packName = packName;
        _isCompactLayout = isCompactLayout;
        RaisePropertyChanged(nameof(PackName));
        RaisePropertyChanged(nameof(SearchPlaceholder));
        RaisePropertyChanged(nameof(LoadingText));
        RaisePropertyChanged(nameof(EmptyText));
    }

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
            var packs = await _resourcePackService.ScanAsync(_instance, _folder, _disposeCancellation.Token);
            if (_isDisposed) return;
            foreach (var item in Items) item.Dispose();
            Items.Clear();
            RaiseSelectionProperties();
            foreach (var pack in packs) Items.Add(new ResourcePackItem(pack, _isCompactLayout));
            ApplyFilter();
        }
        catch (OperationCanceledException exception) { Logger.Debug($"[ResourcePacks] {_packName} scan cancelled: {exception}"); }
        finally
        {
            if (!_isDisposed) { IsLoading = false; RaiseListProperties(); }
        }
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel || _instance == null)
            return;

        await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(_instance.GetSpecialFolder(_folder)));
    }

    private async void Import_OnClick(object? sender, RoutedEventArgs e) => await ImportAsync(null);
    private void Resource_OnDragOver(object? sender, DragEventArgs e)
    {
        var extensions = _instance?.IsBedrock == true ? new[] { ".mcpack", ".mcaddon" } : new[] { ".zip" };
        if (( _instance?.IsBedrock == true ? BedrockResourceImport.Accepts(e.DataTransfer, extensions) : JavaResourceImport.Accepts(e.DataTransfer, extensions))) { e.DragEffects = DragDropEffects.Copy; e.Handled = true; }
    }
    private async void Resource_OnDrop(object? sender, DragEventArgs e) => await ImportAsync(e);
    private async Task ImportAsync(DragEventArgs? drop)
    {
        if (_instance == null) return;
        var refresh = async () => { _hasLoaded = false; await LoadAsync(); };
        if (_instance.IsBedrock)
        {
            var expectedType = _folder == MinecraftSpecialFolder.BehaviorPacksFolder
                ? BedrockPackageContentType.BehaviorPack
                : _folder == MinecraftSpecialFolder.SkinPacksFolder
                    ? BedrockPackageContentType.SkinPack
                    : BedrockPackageContentType.ResourcePack;
            if (drop == null) await BedrockResourceImport.SelectAndImportAsync(this, _instance, $"选择{PackName}", PackName, [".mcpack", ".mcaddon"], null, expectedType, refresh);
            else await BedrockResourceImport.ImportDropAsync(this, drop, _instance, PackName, [".mcpack", ".mcaddon"], null, expectedType, refresh);
            return;
        }
        var destination = _instance.GetSpecialFolder(_folder);
        if (drop == null) await JavaResourceImport.SelectAndImportAsync(this, $"选择{PackName}", destination, PackName, [".zip"], false, refresh);
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
        var filtered = string.IsNullOrWhiteSpace(_filter) ? Items : Items.Where(item =>
            item.DisplayName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
            item.FileName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
            item.DescriptionText.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        FilteredItems.Clear();
        foreach (var item in filtered) FilteredItems.Add(item);
        RaiseListProperties();
    }

    private void ResourcePackCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Control).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed ||
            (sender as Control)?.DataContext is not ResourcePackItem item) return;
        item.IsSelected = !item.IsSelected;
        RaiseSelectionProperties();
    }

    private void SelectAll_OnClick(object? sender, RoutedEventArgs e) => SetSelection(item => true);
    private void ClearSelection_OnClick(object? sender, RoutedEventArgs e) => SetSelection(item => false);
    private void InvertSelection_OnClick(object? sender, RoutedEventArgs e) => SetSelection(item => !item.IsSelected);

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
        await OverlayDialog.ShowStandardAsync(new TextBlock { Margin = new Thickness(24),
            Text = item.DetailsText,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap }, null, this.TryGetHostId(),
            new OverlayDialogOptions { Title = $"{PackName}详情", Buttons = DialogButton.OK, CanResize = false });
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
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Info.FilePath}\"") { UseShellExecute = true });
            return;
        }
        await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(Path.GetDirectoryName(item.Info.FilePath)!));
    }

    private async Task<DialogResult> ConfirmDeleteAsync(string message) => await OverlayDialog.ShowStandardAsync(new TextBlock
    {
        Margin = new Thickness(24), Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap
    }, null, this.TryGetHostId(), new OverlayDialogOptions { Title = $"删除{PackName}", Mode = DialogMode.Error,
        Buttons = DialogButton.YesNo, OverrideYesButtonText = "删除", OverrideNoButtonText = "取消", CanLightDismiss = false, CanResize = false });

    private async Task DeleteAsync(IEnumerable<ResourcePackItem> items)
    {
        var failed = 0;
        foreach (var item in items) try
        {
            if (item.Info.IsBedrock) Directory.Delete(item.Info.FilePath, true);
            else File.Delete(item.Info.FilePath);
        }
        catch (IOException exception) { Logger.Warning($"[ResourcePacks] Failed to delete {item.Info.FilePath}: {exception}"); failed++; }
        catch (UnauthorizedAccessException exception) { Logger.Warning($"[ResourcePacks] Failed to delete {item.Info.FilePath}: {exception}"); failed++; }
        _hasLoaded = false;
        await LoadAsync();
        if (TopLevel.GetTopLevel(this) is { } topLevel)
            NotificationGateway.Notice(topLevel, failed == 0 ? $"已删除所选{PackName}" : $"删除完成，但有 {failed} 个{PackName}操作失败", failed == 0 ? NotificationType.Success : NotificationType.Warning);
    }

    private static ResourcePackItem? GetItem(object? sender) => (sender as Control)?.Tag as ResourcePackItem;
    private void SetSelection(Func<ResourcePackItem, bool> selection) { foreach (var item in Items) item.IsSelected = selection(item); RaiseSelectionProperties(); }
    private void RaiseListProperties() { RaisePropertyChanged(nameof(IsEmpty)); RaisePropertyChanged(nameof(ResourcePackCountText)); }
    private void RaiseSelectionProperties() { RaisePropertyChanged(nameof(SelectedCount)); RaisePropertyChanged(nameof(SelectedCountText)); RaisePropertyChanged(nameof(HasMultipleSelection)); }
    public new event PropertyChangedEventHandler? PropertyChanged;
    private void RaisePropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    public void Dispose() { if (_isDisposed) return; _isDisposed = true; _disposeCancellation.Cancel(); foreach (var item in Items) item.Dispose(); _disposeCancellation.Dispose(); }
}

public sealed class BehaviorPacks(MinecraftInstance instance) : ResourcePacks(instance,
    MinecraftSpecialFolder.BehaviorPacksFolder, "行为包");

public sealed class SkinPacks(MinecraftInstance instance) : ResourcePacks(instance,
    MinecraftSpecialFolder.SkinPacksFolder, "皮肤包", true);

public sealed class ResourcePackItem(ResourcePackInfo info, bool isCompactLayout = false) : INotifyPropertyChanged, IDisposable
{
    private bool _isSelected;
    public ResourcePackInfo Info { get; } = info;
    public string DisplayName => Info.DisplayName;
    public string FileName => Info.FileName;
    public bool IsCompactLayout { get; } = isCompactLayout;
    public string SecondaryText => IsCompactLayout
        ? Info.SkinCount is int count ? $"包含 {count} 个皮肤" : "皮肤数量未知"
        : Info.IsBedrock ? $"最低支持版本：{Info.MinEngineVersion ?? "未知"}" : FileName;
    public string DescriptionText => string.IsNullOrWhiteSpace(Info.Description) ? "没有可用的资源包描述" : Info.Description;
    public string SupportedFormatsText => Info.SupportedFormats ?? "未知";
    public string VersionLabel => Info.IsBedrock ? "版本:" : "支持格式:";
    public string DetailsText => IsCompactLayout
        ? $"名称：{DisplayName}\n文件夹：{FileName}\nUUID：{Info.Uuid ?? "未知"}\n版本：{SupportedFormatsText}\n皮肤：{DescriptionText}"
        : Info.IsBedrock
        ? $"名称：{DisplayName}\n文件夹：{FileName}\nUUID：{Info.Uuid ?? "未知"}\n版本：{SupportedFormatsText}\n最低引擎版本：{Info.MinEngineVersion ?? "未知"}\n作者：{(Info.Authors.Count == 0 ? "未知" : string.Join("、", Info.Authors))}\n模块：{(Info.Modules.Count == 0 ? "无" : string.Join("、", Info.Modules))}\n依赖：{(Info.Dependencies.Count == 0 ? "无" : string.Join("、", Info.Dependencies))}\n子包：{(Info.Subpacks.Count == 0 ? "无" : string.Join("、", Info.Subpacks))}\n能力：{(Info.Capabilities.Count == 0 ? "无" : string.Join("、", Info.Capabilities))}\n\n{DescriptionText}"
        : $"名称：{DisplayName}\n文件：{FileName}\n支持格式：{SupportedFormatsText}\n\n{DescriptionText}";
    public Bitmap? Icon { get; } = CreateIcon(info.IconData);
    public bool HasIcon => Icon != null;
    public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } } }
    private static Bitmap? CreateIcon(byte[]? data) { if (data == null) return null; try { return new Bitmap(new MemoryStream(data)); } catch (InvalidDataException) { return null; } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Dispose() => Icon?.Dispose();
}

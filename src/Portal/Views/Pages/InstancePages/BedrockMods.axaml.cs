using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Portal.Bedrock.Standard.Manifest;
using Portal.Core.Minecraft.Classes;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class BedrockMods : UserControl, INotifyPropertyChanged
{
    private readonly MinecraftInstance? _instance;
    private bool _hasLoaded, _isLoading;
    private string _filter = string.Empty;
    private ResourceSortMode _sortMode = ResourceSortMode.FileName;
    private ResourceFilterMode _filterMode = ResourceFilterMode.All;

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
    public string CountText => IsLoading ? string.Empty : $"{FilteredItems.Count} 个";
    public int SelectedCount => Items.Count(item => item.IsSelected);
    public string SelectedCountText => $"批量操作{SelectedCount}个";
    public bool HasSelection => SelectedCount > 0;

    public BedrockMods()
    {
        InitializeComponent();
        DataContext = this;
        FilterOptions.Add(new ResourceFilterOption(ResourceListUi.BuildFilterLabel("全部", 0)));
        FilterOptions.Add(new ResourceFilterOption(ResourceListUi.BuildFilterLabel("启用", 0)));
        FilterOptions.Add(new ResourceFilterOption(ResourceListUi.BuildFilterLabel("禁用", 0)));
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, Resource_OnDragOver);
        AddHandler(DragDrop.DropEvent, Resource_OnDrop);
    }

    public BedrockMods(MinecraftInstance instance) : this() => _instance = instance;

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
            1 => ResourceFilterMode.Enabled,
            2 => ResourceFilterMode.Disabled,
            3 => ResourceFilterMode.Duplicates,
            _ => ResourceFilterMode.All
        };
        ApplyFilter();
    }

    private BedrockInstanceConfig? Config => _instance?.BedrockConfig;

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
            ShowNotice($"读取模组失败：{exception.Message}", NotificationType.Error);
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

    private bool MatchesStateFilter(BedrockModItem item) => _filterMode switch
    {
        ResourceFilterMode.Enabled => item.IsEnabled,
        ResourceFilterMode.Disabled => item.IsDisabled,
        _ => true
    };

    private IEnumerable<BedrockModItem> SortItems(IEnumerable<BedrockModItem> source) => _sortMode switch
    {
        ResourceSortMode.Name => source.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase),
        ResourceSortMode.LastWriteTime => source.OrderByDescending(item => item.LastWriteTime),
        ResourceSortMode.FileSize => source.OrderByDescending(item => item.Info.FileSize),
        _ => source.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
    };

    private void RefreshFilterOptions()
    {
        FilterOptions[0].Label = ResourceListUi.BuildFilterLabel("全部", Items.Count);
        FilterOptions[1].Label = ResourceListUi.BuildFilterLabel("启用", Items.Count(item => item.IsEnabled));
        FilterOptions[2].Label = ResourceListUi.BuildFilterLabel("禁用", Items.Count(item => item.IsDisabled));
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } topLevel && Config != null)
            await topLevel.Launcher.LaunchDirectoryInfoAsync(
                new DirectoryInfo(BedrockModManager.GetModsFolder(Config)));
    }

    private async void Import_OnClick(object? sender, RoutedEventArgs e) => await ImportAsync(null);

    private void Resource_OnDragOver(object? sender, DragEventArgs e)
    {
        if (JavaResourceImport.Accepts(e.DataTransfer, ".dll"))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void Resource_OnDrop(object? sender, DragEventArgs e) => await ImportAsync(e);

    private async Task ImportAsync(DragEventArgs? drop)
    {
        if (Config == null) return;
        var folder = BedrockModManager.GetModsFolder(Config);
        if (drop == null)
            await JavaResourceImport.SelectAndImportAsync(this, "选择 DLL 模组", folder, "DLL 模组", [".dll"], false,
                RefreshAsync);
        else await JavaResourceImport.ImportDropAsync(this, drop, folder, "DLL 模组", [".dll"], false, RefreshAsync);
    }

    private void Title_OnPointerPressed(object? sender, PointerPressedEventArgs e) => _ = RefreshAsync();

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

    private void SelectAll_OnClick(object? sender, RoutedEventArgs e) => SetSelection(_ => true);
    private void ClearSelection_OnClick(object? sender, RoutedEventArgs e) => SetSelection(_ => false);
    private void InvertSelection_OnClick(object? sender, RoutedEventArgs e) => SetSelection(item => !item.IsSelected);

    private async void EnableSelected_OnClick(object? sender, RoutedEventArgs e) =>
        await SetEnabledAsync(GetSelected(), true);

    private async void DisableSelected_OnClick(object? sender, RoutedEventArgs e) =>
        await SetEnabledAsync(GetSelected(), false);

    private async void EnableMod_OnClick(object? sender, RoutedEventArgs e) =>
        await SetEnabledAsync(Item(sender) is { } item ? [item] : [], true);

    private async void DisableMod_OnClick(object? sender, RoutedEventArgs e) =>
        await SetEnabledAsync(Item(sender) is { } item ? [item] : [], false);

    private async Task SetEnabledAsync(IEnumerable<BedrockModItem> items, bool enabled)
    {
        if (Config == null) return;
        foreach (var item in items) BedrockModManager.Update(Config, item.FileName, entry => entry.Enabled = enabled);
        await RefreshAsync();
        ShowNotice($"已{(enabled ? "启用" : "禁用")}所选模组", NotificationType.Success);
    }

    private async void ShowDetails_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Item(sender) is not { } item || Config == null) return;
        var preload = new CheckBox { Content = "预加载（在游戏初始化早期加载）", IsChecked = item.Info.Config.Preload };
        var delay = new Avalonia.Controls.NumericUpDown
        {
            Minimum = 0, Maximum = BedrockModManager.MaximumDelayMs, Value = item.Info.Config.DelayMs,
            IsEnabled = !item.Info.Config.Preload, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        preload.IsCheckedChanged += (_, _) => delay.IsEnabled = preload.IsChecked != true;
        var panel = new StackPanel
        {
            Margin = new Thickness(10), Spacing = 8, MinWidth = 360,
            Children =
            {
                new TextBlock
                {
                    Text = $"文件名：{item.FileName}\n大小：{item.FileSizeText}",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                preload, new TextBlock { Text = "延迟加载时间（毫秒）" }, delay,
                new TextBlock
                {
                    Text = "预加载模组不使用延迟时间。", Foreground = Avalonia.Media.Brushes.Gray,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                }
            }
        };
        var result = await OverlayDialog.ShowStandardAsync(panel, null, this.TryGetHostId(),
            new OverlayDialogOptions
            {
                Title = "模组详情", Buttons = DialogButton.YesNo, OverrideYesButtonText = "保存", OverrideNoButtonText = "取消",
                CanResize = false, 
            });
        if (result != DialogResult.Yes) return;
        BedrockModManager.Update(Config, item.FileName, entry =>
        {
            entry.Preload = preload.IsChecked == true;
            entry.DelayMs = (int)(delay.Value ?? BedrockModManager.DefaultDelayMs);
        });
        await RefreshAsync();
        ShowNotice("模组设置已保存", NotificationType.Success);
    }

    private async void DeleteSelected_OnClick(object? sender, RoutedEventArgs e) =>
        await ConfirmDeleteAsync(GetSelected());

    private async void DeleteMod_OnClick(object? sender, RoutedEventArgs e) =>
        await ConfirmDeleteAsync(Item(sender) is { } item ? [item] : []);

    private async Task ConfirmDeleteAsync(BedrockModItem[] items)
    {
        if (items.Length == 0) return;
        var result = await OverlayDialog.ShowStandardAsync(
            new TextBlock
            {
                Margin = new Thickness(24), Text = $"确定永久删除选中的 {items.Length} 个模组吗？此操作无法撤销。",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            }, null, this.TryGetHostId(),
            new OverlayDialogOptions
            {
                Title = "删除模组", Mode = DialogMode.Error, Buttons = DialogButton.YesNo, OverrideYesButtonText = "删除",
                OverrideNoButtonText = "取消", CanLightDismiss = false, CanResize = false
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
        ShowNotice(failed == 0 ? "已删除所选模组" : $"删除完成，但有 {failed} 个模组操作失败",
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

    private BedrockModItem[] GetSelected() => Items.Where(item => item.IsSelected).ToArray();
    private static BedrockModItem? Item(object? sender) => (sender as Control)?.Tag as BedrockModItem;

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
        if (TopLevel.GetTopLevel(this) is { } topLevel) NotificationGateway.Notice(topLevel, message, type);
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class BedrockModItem(BedrockModInfo info) : INotifyPropertyChanged
{
    private bool _isSelected;
    public BedrockModInfo Info { get; } = info;
    public string FileName => Info.FileName;
    public string FileSizeText => ResourceListUi.FormatSize(Info.FileSize);
    public string SizeAndNameText => $"{FileSizeText}·{FileName}";
    public DateTime LastWriteTime => ReadLastWriteTime(Info.FilePath);
    public string LastWriteTimeText => $"加入于 {LastWriteTime:yyyy-MM-dd HH:mm}";
    public bool IsEnabled => Info.Config.Enabled;
    public bool IsDisabled => !IsEnabled;
    public bool IsPreload => Info.Config.Preload;
    public bool IsDelayed => IsEnabled && !IsPreload;
    public string DelayText => $"{Info.Config.DelayMs} ms";

    private static DateTime ReadLastWriteTime(string path)
    {
        try { return new FileInfo(path).LastWriteTime; }
        catch { return DateTime.MinValue; }
    }

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
}
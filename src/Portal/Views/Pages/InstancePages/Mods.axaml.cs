using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using AsyncImageLoader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Flurl.Http;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Models;
using Portal.Core.Minecraft.Services;
using Portal.Module.Imaging;
using Portal.Views.Pages.DownloadPages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;
using AutoCompleteBox = Avalonia.Controls.AutoCompleteBox;

namespace Portal.Views.Pages.InstancePages;

public partial class Mods : UserControl, INotifyPropertyChanged, IDisposable
{
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly HashSet<string> _duplicateHashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _duplicateProjectIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly MinecraftInstance? _instance;
    private readonly ModService _modService = new();
    private string _filter = string.Empty;
    private ResourceFilterMode _filterMode = ResourceFilterMode.All;
    private bool _hasLoaded;
    private bool _isDisposed;
    private bool _isLoading;
    private bool _isLoadingMetadata;
    private int _loadVersion;
    private ResourceSortMode _sortMode = ResourceSortMode.FileName;

    public Mods()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, Resource_OnDragOver);
        AddHandler(DragDrop.DropEvent, Resource_OnDrop);
        SelectAllCommand = new RelayCommand(() => SetSelection(item => true));
        ClearSelectionCommand = new RelayCommand(() => SetSelection(item => false));
        InvertSelectionCommand = new RelayCommand(() => SetSelection(item => !item.IsSelected));
        DataContext = this;
        InitializeFilterOptions();
        KeyBindings.Add(new KeyBinding
        {
            Command = new RelayCommand(() => SetSelection(item => true), () => !IsTextInputFocused()),
            Gesture = KeyGesture.Parse("ctrl+A")
        });
        KeyBindings.Add(new KeyBinding
        {
            Command = ClearSelectionCommand,
            Gesture = KeyGesture.Parse("ctrl+Shift+A")
        });
        KeyBindings.Add(new KeyBinding
        {
            Command = InvertSelectionCommand,
            Gesture = KeyGesture.Parse("ctrl+I")
        });
    }

    public Mods(MinecraftInstance instance) : this()
    {
        _instance = instance;
    }

    public ObservableCollection<ModItem> Items { get; } = [];
    public ObservableCollection<ModItem> FilteredItems { get; } = [];

    public string[] SortOptions => ResourceListUi.SortOptions;
    public ObservableCollection<ResourceFilterOption> FilterOptions { get; } = [];

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
    public string ModCountText => $"{FilteredItems.Count} 个";

    public bool IsLoadingMetadata
    {
        get => _isLoadingMetadata;
        private set
        {
            if (_isLoadingMetadata == value) return;
            _isLoadingMetadata = value;
            RaisePropertyChanged(nameof(IsLoadingMetadata));
        }
    }

    public int SelectedCount => Items.Count(item => item.IsSelected);
    public string SelectedCountText => $"批量操作{SelectedCount}个";
    public bool HasMultipleSelection => SelectedCount >= 1;
    public IRelayCommand SelectAllCommand { get; }
    public IRelayCommand ClearSelectionCommand { get; }
    public IRelayCommand InvertSelectionCommand { get; }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _disposeCancellation.Cancel();
        foreach (var item in Items)
            item.Dispose();
        Items.Clear();
        FilteredItems.Clear();
        _disposeCancellation.Dispose();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnsureDefaultSelections();
        Logger.Info($"[Mods] Page attached for instance {_instance?.InstanceName} at {_instance?.FolderPath}.");
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

    private async Task LoadAsync()
    {
        if (_hasLoaded || _instance == null) return;

        _hasLoaded = true;
        var stopwatch = Stopwatch.StartNew();
        Logger.Info(
            $"[Mods] Scanning mods for {_instance.InstanceName} at {_instance.GetSpecialFolder(MinecraftSpecialFolder.ModsFolder)}.");
        var version = ++_loadVersion;
        IsLoading = true;
        RaiseListProperties();
        var mods = await _modService.ScanAsync(_instance, _disposeCancellation.Token);
        if (_isDisposed || version != _loadVersion)
            return;
        foreach (var item in Items) item.Dispose();
        Items.Clear();
        FilteredItems.Clear();
        RaiseSelectionProperties();
        foreach (var batch in mods.Chunk(25))
        {
            foreach (var mod in batch)
                Items.Add(new ModItem(mod));
            await Dispatcher.UIThread.InvokeAsync(() => { },
                DispatcherPriority.Background);
            if (_isDisposed || version != _loadVersion) return;
        }

        ApplyFilter();
        IsLoading = false;
        RaiseListProperties();
        Logger.Info($"[Mods] Scanned {Items.Count} mod(s) for {_instance.InstanceName} in {stopwatch.Elapsed}.");
        _ = Task.Run(() => RefreshMetadataAndFriendlyNamesAsync(mods, _disposeCancellation.Token),
            _disposeCancellation.Token);
    }

    private async Task RefreshMetadataAndFriendlyNamesAsync(IReadOnlyList<ModInfo> mods,
        CancellationToken cancellationToken)
    {
        _ = CacheFriendlyNamesQuietlyAsync(mods, cancellationToken);
        await RefreshMetadataAsync(mods, cancellationToken);
    }

    private async Task CacheFriendlyNamesQuietlyAsync(IEnumerable<ModInfo> mods, CancellationToken cancellationToken)
    {
        try
        {
            await _modService.CacheFriendlyNamesAsync(mods, WikiEntries.FindChineseName,
                updated => Dispatcher.UIThread.Post(() =>
                {
                    if (!_isDisposed)
                        Items.FirstOrDefault(candidate => candidate.Info.FilePath == updated.FilePath)?.Update(updated);
                }, DispatcherPriority.Background), cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[Mods] Friendly-name caching cancelled: {exception}");
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Mods] Friendly-name caching failed: {exception}");
        }
    }

    private async Task RefreshMetadataAsync(IReadOnlyList<ModInfo> mods, CancellationToken cancellationToken)
    {
        try
        {
            await _modService.RefreshMetadataAsync(mods, WikiEntries.FindChineseName,
                updated => Dispatcher.UIThread.Post(() =>
                {
                    if (_isDisposed)
                        return;
                    Items.FirstOrDefault(candidate => candidate.Info.FilePath == updated.FilePath)?.Update(updated);
                }, DispatcherPriority.Background), isLoading => Dispatcher.UIThread.Post(() =>
                {
                    if (!_isDisposed)
                        IsLoadingMetadata = isLoading;
                }, DispatcherPriority.Background), cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[Mods] Metadata refresh cancelled: {exception}");
        }
        catch (FlurlHttpException exception)
        {
            var statusCode = exception.Call.Response?.StatusCode;
            Logger.Error(exception);
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
        }
    }

    private void InitializeFilterOptions()
    {
        FilterOptions.Clear();
        FilterOptions.Add(new ResourceFilterOption(ResourceListUi.BuildFilterLabel("全部", 0)));
        FilterOptions.Add(new ResourceFilterOption(ResourceListUi.BuildFilterLabel("启用", 0)));
        FilterOptions.Add(new ResourceFilterOption(ResourceListUi.BuildFilterLabel("禁用", 0)));
        FilterOptions.Add(new ResourceFilterOption(ResourceListUi.BuildFilterLabel("重复", 0)));
    }

    private void ApplyFilter()
    {
        BuildDuplicateSets();
        var query = Items.Where(MatchesSearchFilter).Where(MatchesStateFilter);
        FilteredItems.Clear();
        foreach (var item in SortItems(query))
        {
            item.IsDuplicate = IsDuplicate(item);
            FilteredItems.Add(item);
        }

        RefreshFilterOptions();
        RaiseListProperties();
    }

    private bool MatchesSearchFilter(ModItem item)
    {
        return string.IsNullOrWhiteSpace(_filter) ||
               item.DisplayName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
               item.FileName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
               item.FriendlyName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
               item.DescriptionText.Contains(_filter, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesStateFilter(ModItem item)
    {
        return _filterMode switch
        {
            ResourceFilterMode.All => true,
            ResourceFilterMode.Enabled => item.IsEnabled,
            ResourceFilterMode.Disabled => item.IsDisabled,
            ResourceFilterMode.Duplicates => IsDuplicate(item),
            _ => true
        };
    }

    private IEnumerable<ModItem> SortItems(IEnumerable<ModItem> source)
    {
        return _sortMode switch
        {
            ResourceSortMode.Name => source.OrderBy(item => item.FriendlyName, StringComparer.OrdinalIgnoreCase),
            ResourceSortMode.LastWriteTime => source.OrderByDescending(item => item.Info.LastWriteTime),
            ResourceSortMode.FileSize => source.OrderByDescending(item => item.Info.FileSize),
            _ => source.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
        };
    }

    private void BuildDuplicateSets()
    {
        _duplicateProjectIds.Clear();
        _duplicateHashes.Clear();
        foreach (var group in Items.Where(item => DuplicateProjectKey(item) != null)
                     .GroupBy(DuplicateProjectKey).Where(group => group.Count() > 1))
            _duplicateProjectIds.Add(group.Key!);
        foreach (var group in Items.Where(item => item.Info.Sha1 is { Length: > 0 })
                     .GroupBy(item => item.Info.Sha1!).Where(group => group.Count() > 1))
            _duplicateHashes.Add(group.Key);
    }

    private static string? DuplicateProjectKey(ModItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Info.Source) || string.IsNullOrWhiteSpace(item.Info.ProjectId))
            return null;
        return $"{item.Info.Source}|{item.Info.ProjectId}".ToLowerInvariant();
    }

    private bool IsDuplicate(ModItem item)
    {
        return (DuplicateProjectKey(item) is { } key && _duplicateProjectIds.Contains(key)) ||
               (item.Info.Sha1 is { Length: > 0 } sha1 && _duplicateHashes.Contains(sha1));
    }

    private void RefreshFilterOptions()
    {
        if (FilterOptions.Count == 0)
            InitializeFilterOptions();
        FilterOptions[0].Label = ResourceListUi.BuildFilterLabel("全部", Items.Count);
        FilterOptions[1].Label = ResourceListUi.BuildFilterLabel("启用", Items.Count(item => item.IsEnabled));
        FilterOptions[2].Label = ResourceListUi.BuildFilterLabel("禁用", Items.Count(item => item.IsDisabled));
        FilterOptions[3].Label = ResourceListUi.BuildFilterLabel("重复", Items.Count(IsDuplicate));
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel || _instance == null) return;
        await topLevel.Launcher.LaunchDirectoryInfoAsync(
            new DirectoryInfo(_instance.GetSpecialFolder(MinecraftSpecialFolder.ModsFolder)));
    }

    private async void Import_OnClick(object? sender, RoutedEventArgs e)
    {
        await ImportAsync(null);
    }

    private void Resource_OnDragOver(object? sender, DragEventArgs e)
    {
        if (JavaResourceImport.Accepts(e.DataTransfer, ".jar"))
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
        var destination = _instance.GetSpecialFolder(MinecraftSpecialFolder.ModsFolder);
        Logger.Info($"[Mods] Importing mod(s) into {destination} for {_instance.InstanceName}.");
        if (drop == null)
            await JavaResourceImport.SelectAndImportAsync(this, "选择模组", destination, "模组", [".jar"], false, refresh);
        else await JavaResourceImport.ImportDropAsync(this, drop, destination, "模组", [".jar"], false, refresh);
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

    private void ModCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Control).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed ||
            (sender as Control)?.DataContext is not ModItem item)
            return;

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

    private async void EnableSelected_OnClick(object? sender, RoutedEventArgs e)
    {
        await SetSelectedDisabledAsync(false);
    }

    private async void DisableSelected_OnClick(object? sender, RoutedEventArgs e)
    {
        await SetSelectedDisabledAsync(true);
    }

    private async void DeleteSelected_OnClick(object? sender, RoutedEventArgs e)
    {
        var selected = GetSelectedItems();
        if (selected.Length < 1)
            return;

        var result = await OverlayDialog.ShowStandardAsync(
            new TextBlock
            {
                Margin = new Thickness(24), Text = $"确定要永久删除选中的 {selected.Length} 个模组吗？此操作无法撤销。",
                TextWrapping = TextWrapping.Wrap
            },
            null, this.TryGetHostId(), new OverlayDialogOptions
            {
                Title = "删除模组", Mode = DialogMode.Error, Buttons = DialogButton.YesNo,
                OverrideYesButtonText = "删除", OverrideNoButtonText = "取消", CanLightDismiss = false, CanResize = false
            });
        if (result != DialogResult.Yes)
            return;

        await RunSelectedFileActionAsync(selected, item => File.Delete(item.Info.FilePath), null, "删除");
    }

    private void ShowModDetails_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetModItem(sender) is not { Info.ProjectId: { Length: > 0 } projectId, Info.Source: { } source } item ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var detailSource = source == "Modrinth" ? ModDetailsSource.Modrinth :
            source == "CurseForge" ? ModDetailsSource.CurseForge : (ModDetailsSource?)null;
        if (detailSource is null)
        {
            ShowNotice("尚未识别此模组的平台信息，暂时无法查看详情", NotificationType.Warning);
            return;
        }


        ModDetailsPage.Open(topLevel, new ModDetailsTarget(detailSource.Value, projectId), item.FriendlyName);
    }

    private async void EnableMod_OnClick(object? sender, RoutedEventArgs e)
    {
        await SetModDisabledAsync(GetModItem(sender), false);
    }

    private async void DisableMod_OnClick(object? sender, RoutedEventArgs e)
    {
        await SetModDisabledAsync(GetModItem(sender), true);
    }

    private async void DeleteMod_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetModItem(sender) is not { } item)
            return;

        var result = await OverlayDialog.ShowStandardAsync(new TextBlock
        {
            Margin = new Thickness(24), Text = $"确定要永久删除模组“{item.DisplayName}”吗？此操作无法撤销。",
            TextWrapping = TextWrapping.Wrap
        }, null, this.TryGetHostId(), CreateDeleteConfirmationOptions());
        if (result == DialogResult.Yes)
            await RunSelectedFileActionAsync([item], mod => File.Delete(mod.Info.FilePath), null, "删除");
    }

    private async void OpenModFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetModItem(sender) is not { } item || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Info.FilePath}\"")
                { UseShellExecute = true });
            return;
        }

        await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(Path.GetDirectoryName(item.Info.FilePath)!));
    }

    private async Task SetSelectedDisabledAsync(bool disabled)
    {
        var selected = GetSelectedItems().Where(item => item.IsDisabled != disabled).ToArray();
        if (selected.Length == 0)
            return;

        await RunSelectedFileActionAsync(selected, item =>
        {
            var destination = disabled ? item.Info.FilePath + ".disabled" : item.Info.FilePath[..^".disabled".Length];
            File.Move(item.Info.FilePath, destination);
        }, item => item.Info with
        {
            FilePath = disabled ? item.Info.FilePath + ".disabled" : item.Info.FilePath[..^".disabled".Length],
            IsDisabled = disabled
        }, disabled ? "禁用" : "启用");
    }

    private Task SetModDisabledAsync(ModItem? item, bool disabled)
    {
        return item == null || item.IsDisabled == disabled
            ? Task.CompletedTask
            : RunSelectedFileActionAsync([item], mod => File.Move(mod.Info.FilePath,
                    disabled ? mod.Info.FilePath + ".disabled" : mod.Info.FilePath[..^".disabled".Length]),
                mod => mod.Info with
                {
                    FilePath = disabled ? mod.Info.FilePath + ".disabled" : mod.Info.FilePath[..^".disabled".Length],
                    IsDisabled = disabled
                }, disabled ? "禁用" : "启用");
    }

    private Task RunSelectedFileActionAsync(IEnumerable<ModItem> selected, Action<ModItem> action,
        Func<ModItem, ModInfo>? localUpdate, string actionName)
    {
        var failed = 0;
        var selectedItems = selected.ToArray();
        Logger.Info($"[Mods] {actionName} requested for {selectedItems.Length} mod(s) in {_instance?.InstanceName}.");
        foreach (var item in selectedItems)
            try
            {
                action(item);
                if (localUpdate == null)
                {
                    item.Dispose();
                    Items.Remove(item);
                }
                else
                {
                    item.Update(localUpdate(item));
                    item.IsSelected = false;
                }
            }
            catch (IOException exception)
            {
                Logger.Warning($"[Mods] {actionName} failed for {item.Info.FilePath}: {exception}");
                failed++;
            }
            catch (UnauthorizedAccessException exception)
            {
                Logger.Warning($"[Mods] {actionName} failed for {item.Info.FilePath}: {exception}");
                failed++;
            }

        ApplyFilter();
        RaiseSelectionProperties();
        ShowNotice(failed == 0 ? $"已{actionName}所选模组" : $"{actionName}完成，但有 {failed} 个模组操作失败",
            failed == 0 ? NotificationType.Success : NotificationType.Warning);
        Logger.Info($"[Mods] {actionName} completed for {selectedItems.Length} mod(s): {failed} failure(s).");
        return Task.CompletedTask;
    }

    private ModItem[] GetSelectedItems()
    {
        return Items.Where(item => item.IsSelected).ToArray();
    }

    private bool IsTextInputFocused()
    {
        return TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox
            or AutoCompleteBox or TioUi.Controls.AutoCompleteBox;
    }

    private static ModItem? GetModItem(object? sender)
    {
        return (sender as Control)?.Tag as ModItem;
    }

    private static OverlayDialogOptions CreateDeleteConfirmationOptions()
    {
        return new OverlayDialogOptions
        {
            Title = "删除模组", Mode = DialogMode.Error, Buttons = DialogButton.YesNo,
            OverrideYesButtonText = "删除", OverrideNoButtonText = "取消", CanLightDismiss = false, CanResize = false
        };
    }

    private void SetSelection(Func<ModItem, bool> selection)
    {
        foreach (var item in Items)
            item.IsSelected = selection(item);
        RaiseSelectionProperties();
    }

    private void RaiseListProperties()
    {
        RaisePropertyChanged(nameof(IsEmpty));
        RaisePropertyChanged(nameof(ModCountText));
    }

    private void RaiseSelectionProperties()
    {
        RaisePropertyChanged(nameof(SelectedCount));
        RaisePropertyChanged(nameof(SelectedCountText));
        RaisePropertyChanged(nameof(HasMultipleSelection));
    }

    private void ShowNotice(string message, NotificationType type)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
            topLevel.Notice(message, type);
    }

    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public static class WikiEntries
{
    private static readonly Lazy<Dictionary<string, string>> Entries = new(Load);

    public static string? FindChineseName(string curseForgeSlug)
    {
        return Entries.Value.GetValueOrDefault(curseForgeSlug);
    }

    private static Dictionary<string, string> Load()
    {
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Portal.Assets.WikiEntries.txt");
        if (stream == null)
            return entries;

        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            foreach (var entry in line.Split('¨'))
            {
                var separator = entry.IndexOf('|');
                if (separator <= 0 || separator == entry.Length - 1)
                    continue;

                var slugs = entry[..separator];
                if (slugs.StartsWith('@'))
                    continue;

                var curseForgeSlug = slugs.Split('@')[0];
                var chineseName = GetChineseName(entry[(separator + 1)..], curseForgeSlug);
                if (!string.IsNullOrWhiteSpace(curseForgeSlug) && !string.IsNullOrWhiteSpace(chineseName))
                    entries.TryAdd(curseForgeSlug, chineseName);
            }
        }

        return entries;
    }

    private static string GetChineseName(string chineseName, string curseForgeSlug)
    {
        var englishName = string.Join(' ', curseForgeSlug.Split('-')
            .Where(word => word.Length > 0)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
        return Regex.Replace(chineseName.Replace("*", $" ({englishName})"), @"\s*\([^)]*\)\s*$", string.Empty).Trim();
    }
}

public sealed class ModItem(ModInfo info) : INotifyPropertyChanged, IDisposable
{
    private bool _isDuplicate;
    private bool _isSelected;
    public ModInfo Info { get; private set; } = info;

    public string DisplayName => Info.DisplayName;
    public string FriendlyName => Info.FriendlyName ?? Info.DisplayName;
    public string FileName => Info.FileName + ".jar";
    public string SizeAndNameText => $"{ResourceListUi.FormatSize(Info.FileSize)}·{FileName}";
    public string DescriptionText => Info.Description ?? "没有可用的模组描述";
    public string? IconUrl => Info.IconUrl;
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconUrl);
    public IAsyncImageLoader ImageLoader { get; } = new ModImageLoader();
    public bool IsDisabled => Info.IsDisabled;
    public bool IsEnabled => !IsDisabled;

    public bool IsDuplicate
    {
        get => _isDuplicate;
        set
        {
            if (_isDuplicate == value) return;
            _isDuplicate = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDuplicate)));
        }
    }

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

    public void Dispose()
    {
        ImageLoader.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(ModInfo info)
    {
        Info = info;
        foreach (var propertyName in new[]
                 {
                     nameof(DisplayName), nameof(FriendlyName), nameof(FileName), nameof(SizeAndNameText),
                     nameof(DescriptionText),
                     nameof(IconUrl), nameof(HasIcon), nameof(IsDisabled), nameof(IsEnabled)
                 })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
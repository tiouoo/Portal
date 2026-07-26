using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using AsyncImageLoader;
using Portal.Module.Imaging;
using CommunityToolkit.Mvvm.Input;
using Flurl.Http;
using Portal.Const;
using Portal.Views.Pages;
using Portal.Views.Pages.DownloadPages;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class Mods : UserControl, INotifyPropertyChanged, IDisposable
{
    private readonly MinecraftInstance? _instance;
    private readonly ModService _modService = new();
    private bool _hasLoaded;
    private bool _isLoading;
    private bool _isLoadingMetadata;
    private bool _isDisposed;
    private int _loadVersion; // 加载序号，防止并发刷新时旧的扫描结果覆盖新结果
    private string _filter = string.Empty;
    private readonly CancellationTokenSource _disposeCancellation = new();

    public ObservableCollection<ModItem> Items { get; } = [];
    public ObservableCollection<ModItem> FilteredItems { get; } = [];

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
        KeyBindings.Add(new KeyBinding()
        {
            // 文本框聚焦时不拦截 Ctrl+A，保留全选文本的默认行为
            Command = new RelayCommand(() => SetSelection(item => true), () => !IsTextInputFocused()),
            Gesture = KeyGesture.Parse("ctrl+A")
        });
        KeyBindings.Add(new KeyBinding()
        {
            Command = ClearSelectionCommand,
            Gesture = KeyGesture.Parse("ctrl+Shift+A")
        });
        KeyBindings.Add(new KeyBinding()
        {
            Command = InvertSelectionCommand,
            Gesture = KeyGesture.Parse("ctrl+I")
        });
    }

    public Mods(MinecraftInstance instance) : this() => _instance = instance;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_hasLoaded || _instance == null) return;

        _hasLoaded = true;
        var version = ++_loadVersion;
        IsLoading = true;
        RaiseListProperties();
        var mods = await _modService.ScanAsync(_instance, _disposeCancellation.Token);
        if (_isDisposed || version != _loadVersion)
            return;
        Items.Clear();
        RaiseSelectionProperties();
        foreach (var mod in mods)
            Items.Add(new ModItem(mod));
        ApplyFilter();
        IsLoading = false;
        RaiseListProperties();
        _ = RefreshMetadataAndFriendlyNamesAsync(mods, _disposeCancellation.Token);
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
                updated => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!_isDisposed)
                        Items.FirstOrDefault(item => item.Info.FilePath == updated.FilePath)?.Update(updated);
                }), cancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }

    private async Task RefreshMetadataAsync(IReadOnlyList<ModInfo> mods, CancellationToken cancellationToken)
    {
        try
        {
            await _modService.RefreshMetadataAsync(mods, WikiEntries.FindChineseName, updated => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_isDisposed)
                        return;
                    var item = Items.FirstOrDefault(candidate => candidate.Info.FilePath == updated.FilePath);
                    item?.Update(updated);
                }), isLoading => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!_isDisposed)
                        IsLoadingMetadata = isLoading;
                }), cancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (FlurlHttpException exception)
        {
            var statusCode = exception.Call.Response?.StatusCode;
            Logger.Error($"获取模组平台信息失败 ({statusCode?.ToString() ?? "网络错误"}): {exception.Message}");
        }
        catch (Exception exception)
        {
            Logger.Error($"获取模组平台信息失败: {exception.Message}");
        }
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(_filter)
            ? Items
            : Items.Where(item =>
                item.DisplayName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                item.FileName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                item.FriendlyName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                item.DescriptionText.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        FilteredItems.Clear();
        foreach (var item in filtered)
            FilteredItems.Add(item);
        RaiseListProperties();
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel || _instance == null) return;
        await topLevel.Launcher.LaunchDirectoryInfoAsync(
            new DirectoryInfo(_instance.GetSpecialFolder(MinecraftSpecialFolder.ModsFolder)));
    }

    private async void Import_OnClick(object? sender, RoutedEventArgs e) => await ImportAsync(null);
    private void Resource_OnDragOver(object? sender, DragEventArgs e)
    {
        if (JavaResourceImport.Accepts(e.DataTransfer, ".jar")) { e.DragEffects = DragDropEffects.Copy; e.Handled = true; }
    }
    private async void Resource_OnDrop(object? sender, DragEventArgs e) => await ImportAsync(e);
    private async Task ImportAsync(DragEventArgs? drop) 
    {
        if (_instance == null) return;
        var refresh = async () => { _hasLoaded = false; await LoadAsync(); };
        var destination = _instance.GetSpecialFolder(MinecraftSpecialFolder.ModsFolder);
        if (drop == null) await JavaResourceImport.SelectAndImportAsync(this, "选择模组", destination, "模组", [".jar"], false, refresh);
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

    private void SelectAll_OnClick(object? sender, RoutedEventArgs e) => SetSelection(item => true);

    private void ClearSelection_OnClick(object? sender, RoutedEventArgs e) => SetSelection(item => false);

    private void InvertSelection_OnClick(object? sender, RoutedEventArgs e) => SetSelection(item => !item.IsSelected);

    private async void EnableSelected_OnClick(object? sender, RoutedEventArgs e) =>
        await SetSelectedDisabledAsync(false);

    private async void DisableSelected_OnClick(object? sender, RoutedEventArgs e) =>
        await SetSelectedDisabledAsync(true);

    private async void DeleteSelected_OnClick(object? sender, RoutedEventArgs e)
    {
        var selected = GetSelectedItems();
        if (selected.Length < 1)
            return;

        var result = await OverlayDialog.ShowStandardAsync(
            new TextBlock
            {
                Margin = new Thickness(24), Text = $"确定要永久删除选中的 {selected.Length} 个模组吗？此操作无法撤销。",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
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

        // Source and project ID were restored from the installed file's metadata cache.
        // ModDetailsPage fetches the current project record from that provider.
        ModDetailsPage.Open(topLevel, new ModDetailsTarget(detailSource.Value, projectId), item.FriendlyName);
    }

    private async void EnableMod_OnClick(object? sender, RoutedEventArgs e) =>
        await SetModDisabledAsync(GetModItem(sender), false);

    private async void DisableMod_OnClick(object? sender, RoutedEventArgs e) =>
        await SetModDisabledAsync(GetModItem(sender), true);

    private async void DeleteMod_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetModItem(sender) is not { } item)
            return;

        var result = await OverlayDialog.ShowStandardAsync(new TextBlock
        {
            Margin = new Thickness(24), Text = $"确定要永久删除模组“{item.DisplayName}”吗？此操作无法撤销。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
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

    private Task SetModDisabledAsync(ModItem? item, bool disabled) => item == null || item.IsDisabled == disabled
        ? Task.CompletedTask
        : RunSelectedFileActionAsync([item], mod => File.Move(mod.Info.FilePath,
                disabled ? mod.Info.FilePath + ".disabled" : mod.Info.FilePath[..^".disabled".Length]),
            mod => mod.Info with
            {
                FilePath = disabled ? mod.Info.FilePath + ".disabled" : mod.Info.FilePath[..^".disabled".Length],
                IsDisabled = disabled
            }, disabled ? "禁用" : "启用");

    private Task RunSelectedFileActionAsync(IEnumerable<ModItem> selected, Action<ModItem> action,
        Func<ModItem, ModInfo>? localUpdate, string actionName)
    {
        var failed = 0;
        foreach (var item in selected)
        {
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
            catch (IOException)
            {
                failed++;
            }
            catch (UnauthorizedAccessException)
            {
                failed++;
            }
        }

        ApplyFilter();
        RaiseSelectionProperties();
        ShowNotice(failed == 0 ? $"已{actionName}所选模组" : $"{actionName}完成，但有 {failed} 个模组操作失败",
            failed == 0 ? NotificationType.Success : NotificationType.Warning);
        return Task.CompletedTask;
    }

    private ModItem[] GetSelectedItems() => Items.Where(item => item.IsSelected).ToArray();

    private bool IsTextInputFocused() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox or Avalonia.Controls.AutoCompleteBox or TioUi.Controls.AutoCompleteBox;

    private static ModItem? GetModItem(object? sender) => (sender as Control)?.Tag as ModItem;

    private static OverlayDialogOptions CreateDeleteConfirmationOptions() => new()
    {
        Title = "删除模组", Mode = DialogMode.Error, Buttons = DialogButton.YesNo,
        OverrideYesButtonText = "删除", OverrideNoButtonText = "取消", CanLightDismiss = false, CanResize = false
    };

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
            NotificationGateway.Notice(topLevel, message, type);
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void RaisePropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

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
}

public static class WikiEntries
{
    private static readonly Lazy<Dictionary<string, string>> Entries = new(Load);

    public static string? FindChineseName(string curseForgeSlug) =>
        Entries.Value.GetValueOrDefault(curseForgeSlug);

    private static Dictionary<string, string> Load()
    {
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Portal.WikiEntries.txt");
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
    private bool _isSelected;
    private ModInfo _info = info;
    public ModInfo Info => _info;
    public string DisplayName => _info.DisplayName;
    public string FriendlyName => _info.FriendlyName ?? _info.DisplayName;
    public string FileName => _info.FileName + ".jar";
    public string DescriptionText => _info.Description ?? "没有可用的模组描述";
    public string? IconUrl => _info.IconUrl;
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconUrl);
    public IAsyncImageLoader ImageLoader { get; } = new ModImageLoader();
    public bool IsDisabled => _info.IsDisabled;
    public bool IsEnabled => !IsDisabled;

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

    public void Update(ModInfo info)
    {
        _info = info;
        foreach (var propertyName in new[] { nameof(DisplayName), nameof(FriendlyName), nameof(FileName), nameof(DescriptionText),
                     nameof(IconUrl), nameof(HasIcon), nameof(IsDisabled), nameof(IsEnabled) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Dispose() => ImageLoader.Dispose();
}

// ModImageLoader 已移至 Portal.Module.Imaging，改用统一的磁盘缓存加载器。

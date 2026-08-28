using System.Collections.ObjectModel;
using System.ComponentModel;
using AsyncImageLoader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using Portal.Core.Const;
using Portal.Core.Module;
using Portal.Module.Imaging;
using Portal.Localization;
using SkiaSharp;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class Saves : UserControl, INotifyPropertyChanged, IDisposable
{
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly MinecraftInstance? _instance;
    private readonly DispatcherTimer _lockRefreshTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly WorldSaveService _saveService = new();
    private readonly string? _savesPath;
    private FilterSortMenuController? _filterSortMenu;
    private string _filter = string.Empty;
    private ResourceFilterMode _filterMode = ResourceFilterMode.All;
    private bool _hasLoaded;
    private bool _isAttached;
    private bool _isDisposed;
    private bool _isLoading;
    private bool _isRefreshingLockStates;
    private ResourceSortMode _sortMode = ResourceSortMode.FileName;

    private static readonly string[] FilterBaseNames =
    [
        CommonLanguageManager.Instance.mod_all.CurrentValue()
    ];

    public Saves()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, Resource_OnDragOver);
        AddHandler(DragDrop.DropEvent, Resource_OnDrop);
        DataContext = this;
        FilterOptions.Add(new ResourceFilterOption(ResourceListUi.BuildFilterLabel(
            CommonLanguageManager.Instance.mod_all.CurrentValue(), 0)));
        _filterSortMenu = new FilterSortMenuController(FilterSortButton,
            CommonLanguageManager.Instance.resourceList_sortBy.CurrentValue(),
            CommonLanguageManager.Instance.resourceList_filter.CurrentValue(), FilterOptions,
            FilterBaseNames, OnSortSelected, OnFilterSelected);
        _lockRefreshTimer.Tick += LockRefreshTimer_OnTick;
    }

    public Saves(MinecraftInstance instance) : this()
    {
        _instance = instance;
        _savesPath = instance.GetSpecialFolder(MinecraftSpecialFolder.SavesFolder);
    }

    public ObservableCollection<SaveItem> Items { get; } = [];
    public ObservableCollection<SaveItem> FilteredItems { get; } = [];
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
    public string SaveCountText => IsLoading
        ? string.Empty
        : string.Format(CommonLanguageManager.Instance.resourceList_count.CurrentValue(), FilteredItems.Count);

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _disposeCancellation.Cancel();
        _lockRefreshTimer.Stop();
        _lockRefreshTimer.Tick -= LockRefreshTimer_OnTick;
        Items.Clear();
        FilteredItems.Clear();
        DataContext = null;
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnsureDefaultSelections();
        _isAttached = true;
        _ = LoadAsync();
        UpdateLockRefreshTimer();
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
            1 => ResourceFilterMode.Enabled,
            2 => ResourceFilterMode.Disabled,
            3 => ResourceFilterMode.Duplicates,
            _ => ResourceFilterMode.All
        };
        ApplyFilter();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _lockRefreshTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
            UpdateLockRefreshTimer();
    }

    private async Task LoadAsync()
    {
        if (_hasLoaded || _instance == null)
            return;

        _hasLoaded = true;
        IsLoading = true;
        RaiseListProperties();
        IReadOnlyList<WorldSaveInfo> saves;
        try
        {
            saves = await _saveService.ScanAsync(_instance, _disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (_isDisposed)
            return;
        Items.Clear();
        foreach (var save in saves)
            Items.Add(new SaveItem(save, _instance));
        ApplyFilter();
        IsLoading = false;
        RaiseListProperties();
        await RefreshLockStatesAsync();
    }

    private void ApplyFilter()
    {
        var query = string.IsNullOrWhiteSpace(_filter)
            ? Items
            : Items.Where(item => item.FolderName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                                  item.DisplayName.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        FilteredItems.Clear();
        foreach (var item in SortItems(query))
            FilteredItems.Add(item);
        if (FilterOptions.Count == 0)
            FilterOptions.Add(new ResourceFilterOption(ResourceListUi.BuildFilterLabel(
                CommonLanguageManager.Instance.mod_all.CurrentValue(), 0)));
        FilterOptions[0].Label = ResourceListUi.BuildFilterLabel(CommonLanguageManager.Instance.mod_all.CurrentValue(),
            Items.Count);
        _filterSortMenu?.SyncFilterLabels(FilterOptions);
        RaiseListProperties();
    }

    private IEnumerable<SaveItem> SortItems(IEnumerable<SaveItem> source)
    {
        return _sortMode switch
        {
            ResourceSortMode.Name => source.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase),
            ResourceSortMode.LastWriteTime => source.OrderByDescending(item => item.Info.LastWriteTime),
            ResourceSortMode.FileSize => source.OrderByDescending(item => item.FolderSize),
            _ => source.OrderBy(item => item.FolderName, StringComparer.OrdinalIgnoreCase)
        };
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_savesPath))
            await TopLevel.GetTopLevel(this).Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(_savesPath));
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
        if (_savesPath == null) return;
        var refresh = async () =>
        {
            _hasLoaded = false;
            await LoadAsync();
        };
        if (drop == null)
            await JavaResourceImport.SelectAndImportAsync(this,
                CommonLanguageManager.Instance.saves_selectSave.CurrentValue(), _savesPath,
                CommonLanguageManager.Instance.favorite_kindSave.CurrentValue(), [".zip"], true, refresh);
        else
            await JavaResourceImport.ImportDropAsync(this, drop, _savesPath,
                CommonLanguageManager.Instance.favorite_kindSave.CurrentValue(), [".zip"], true, refresh);
    }

    private async void OpenWorldFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is { } item)
            await TopLevel.GetTopLevel(this).Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(item.Info.FolderPath));
    }

    private async void ChangeIcon_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item || !Directory.Exists(item.Info.FolderPath))
            return;
        if (item.Info.IsLocked)
        {
            await ShowLockedAsync();
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = CommonLanguageManager.Instance.saves_selectWorldIcon.CurrentValue(),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(CommonLanguageManager.Instance.saves_image.CurrentValue())
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"]
                }
            ]
        });
        if (files.Count == 0)
            return;

        var iconPath = Path.Combine(item.Info.FolderPath, "icon.png");
        var temporaryIconPath = Path.Combine(item.Info.FolderPath, $".{Guid.NewGuid():N}.png");
        try
        {
            await using var input = await files[0].OpenReadAsync();
            using var image = SKBitmap.Decode(input) ??
                              throw new InvalidDataException(
                                  CommonLanguageManager.Instance.saves_cannotReadImage.CurrentValue());
            var cropSize = Math.Min(image.Width, image.Height);
            var source = new SKRectI((image.Width - cropSize) / 2, (image.Height - cropSize) / 2,
                (image.Width + cropSize) / 2, (image.Height + cropSize) / 2);
            using var surface = SKSurface.Create(new SKImageInfo(64, 64)) ??
                                throw new InvalidOperationException(
                                    CommonLanguageManager.Instance.saves_cannotCreateIcon.CurrentValue());
            surface.Canvas.DrawBitmap(image, source, new SKRect(0, 0, 64, 64), new SKSamplingOptions());
            using var snapshot = surface.Snapshot();
            using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
            await using (var output = File.Create(temporaryIconPath))
            {
                png.SaveTo(output);
            }

            File.Move(temporaryIconPath, iconPath, true);
            RefreshItem(item, item.Info with { IconPath = iconPath });
            await RefreshSavesAsync();
            ShowNotice(CommonLanguageManager.Instance.saves_worldIconChanged.CurrentValue(), NotificationType.Success);
        }
        catch (IOException ex) when (IsFileLocked(ex))
        {
            await ShowLockedAsync();
        }
        catch (IOException ex)
        {
            ShowNotice(string.Format(CommonLanguageManager.Instance.saves_worldIconChangeFailed.CurrentValue(),
                ex.Message), NotificationType.Error);
        }
        catch (UnauthorizedAccessException)
        {
            ShowNotice(CommonLanguageManager.Instance.saves_worldIconNoPermission.CurrentValue(),
                NotificationType.Error);
        }
        catch (Exception ex)
        {
            ShowNotice(string.Format(CommonLanguageManager.Instance.saves_worldIconChangeFailed.CurrentValue(),
                ex.Message), NotificationType.Error);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryIconPath))
                    File.Delete(temporaryIconPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private async void ShowInfo_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item)
            return;
        await ShowInfoAsync(item);
    }

    private void QuickEnter_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item || _instance == null)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var target = new RecentPlayTarget(
            _instance,
            RecentPlayTargetType.World,
            item.Info.FolderName,
            string.IsNullOrWhiteSpace(item.Info.LevelName) ? item.Info.FolderName : item.Info.LevelName,
            string.Format(CommonLanguageManager.Instance.recentPlay_saveDescription.CurrentValue(),
                item.Info.Version ?? CommonLanguageManager.Instance.recentPlay_unknownVersion.CurrentValue(),
                GetGameModeText(item.Info.GameMode)),
            item.Info.LastPlayedTime ?? DateTime.MinValue,
            item.Info.IconPath);

        _ = MinecraftLaunchService.LaunchAsync(_instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(_instance, logSession => MinecraftLogPage.Open(logSession, topLevel)),
            target);
    }

    private async void CreateShortcut_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item || _instance == null)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var target = new RecentPlayTarget(
            _instance,
            RecentPlayTargetType.World,
            item.Info.FolderName,
            string.IsNullOrWhiteSpace(item.Info.LevelName) ? item.Info.FolderName : item.Info.LevelName,
            string.Format(CommonLanguageManager.Instance.recentPlay_saveDescription.CurrentValue(),
                item.Info.Version ?? CommonLanguageManager.Instance.recentPlay_unknownVersion.CurrentValue(),
                GetGameModeText(item.Info.GameMode)),
            item.Info.LastPlayedTime ?? DateTime.MinValue,
            item.Info.IconPath);

        await DesktopShortcutUi.CreateAsync(topLevel, () => DesktopShortcutService.CreateAsync(_instance, target));
    }

    private static string GetGameModeText(int? gameMode)
    {
        return gameMode switch
        {
            0 => CommonLanguageManager.Instance.recentPlay_gameModeSurvival.CurrentValue(),
            1 => CommonLanguageManager.Instance.recentPlay_gameModeCreative.CurrentValue(),
            2 => CommonLanguageManager.Instance.recentPlay_gameModeAdventure.CurrentValue(),
            3 => CommonLanguageManager.Instance.recentPlay_gameModeSpectator.CurrentValue(),
            _ => CommonLanguageManager.Instance.recentPlay_gameModeUnknown.CurrentValue()
        };
    }

    private async void DeleteWorld_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item || !Directory.Exists(item.Info.FolderPath))
            return;
        if (item.Info.IsLocked)
        {
            await ShowLockedAsync();
            return;
        }

        var result = await OverlayDialog.ShowStandardAsync(
            new TextBlock
            {
                Margin = new Thickness(24),
                Text = string.Format(CommonLanguageManager.Instance.saves_deleteConfirm.CurrentValue(),
                    item.DisplayName),
                TextWrapping = TextWrapping.Wrap
            },
            null, this.TryGetHostId(), CreateDeleteConfirmationOptions(
                CommonLanguageManager.Instance.saves_deleteSaveTitle.CurrentValue()));
        if (result != DialogResult.Yes)
            return;
        try
        {
            Directory.Delete(item.Info.FolderPath, true);
            Items.Remove(item);
            ApplyFilter();
            ShowNotice(CommonLanguageManager.Instance.saves_saveDeleted.CurrentValue(), NotificationType.Success);
        }
        catch (IOException ex) when (IsFileLocked(ex))
        {
            await ShowLockedAsync();
        }
        catch (IOException ex)
        {
            ShowNotice(string.Format(CommonLanguageManager.Instance.saves_deleteFailed.CurrentValue(), ex.Message),
                NotificationType.Error);
        }
        catch (UnauthorizedAccessException)
        {
            ShowNotice(CommonLanguageManager.Instance.saves_deleteNoPermission.CurrentValue(), NotificationType.Error);
        }
    }

    private Task ShowLockedAsync()
    {
        ShowNotice(CommonLanguageManager.Instance.saves_worldLocked.CurrentValue(), NotificationType.Warning);
        return Task.CompletedTask;
    }

    private void ShowNotice(string message, NotificationType type)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
            topLevel.Notice(message, type);
    }

    private static OverlayDialogOptions CreateDeleteConfirmationOptions(string title)
    {
        return new OverlayDialogOptions
        {
            Title = title,
            Mode = DialogMode.Error,
            Buttons = DialogButton.YesNo,
            OverrideYesButtonText = CommonLanguageManager.Instance.dashboard_delete.CurrentValue(),
            OverrideNoButtonText = CommonLanguageManager.Instance.common_cancel.CurrentValue(),
            CanLightDismiss = false,
            CanResize = false
        };
    }

    private static SaveItem? GetItem(object? sender)
    {
        return (sender as Control)?.Tag as SaveItem;
    }

    private static bool IsFileLocked(IOException exception)
    {
        return (exception.HResult & 0xffff) is 32 or 33;
    }

    private async Task RefreshLockStatesAsync()
    {
        if (_isDisposed || _isRefreshingLockStates || !IsVisible || Items.Count == 0)
            return;

        _isRefreshingLockStates = true;
        try
        {
            var items = Items.ToArray();
            var lockStates = await Task.WhenAll(items.Select(async item =>
                (Item: item, IsLocked: await _saveService.IsWorldLockedAsync(item.Info.FolderPath))));
            if (_isDisposed) return;
            var changed = false;
            foreach (var (item, isLocked) in lockStates)
            {
                if (item.Info.IsLocked == isLocked)
                    continue;

                RefreshItem(item, item.Info with { IsLocked = isLocked });
                changed = true;
            }

            if (changed)
                ApplyFilter();
        }
        finally
        {
            _isRefreshingLockStates = false;
        }
    }

    private async void LockRefreshTimer_OnTick(object? sender, EventArgs e)
    {
        try
        {
            await RefreshLockStatesAsync();
        }
        catch when (_isDisposed)
        {
        }
    }

    private void UpdateLockRefreshTimer()
    {
        if (!_isAttached || !IsVisible)
        {
            _lockRefreshTimer.Stop();
            return;
        }

        _lockRefreshTimer.Start();
        _ = RefreshLockStatesAsync();
    }

    private void RefreshItem(SaveItem item, WorldSaveInfo info)
    {
        var index = Items.IndexOf(item);
        if (index < 0)
            return;

        Items[index] = new SaveItem(info, _instance);
    }

    private void SearchBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        _filter = (sender as TextBox)?.Text ?? string.Empty;
        ApplyFilter();
    }

    private void Title_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = RefreshSavesAsync();
    }

    private async Task RefreshSavesAsync()
    {
        _hasLoaded = false;
        await LoadAsync();
    }

    private void RaiseListProperties()
    {
        RaisePropertyChanged(nameof(IsEmpty));
        RaisePropertyChanged(nameof(SaveCountText));
    }

    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Control).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed ||
            (sender as Control)?.DataContext is not SaveItem item)
            return;

        _ = ShowInfoAsync(item);
    }

    private Task ShowInfoAsync(SaveItem item)
    {
        return OverlayDialog.ShowCustomAsync<WorldSaveDetails, WorldSaveDetailsViewModel, object>(
            new WorldSaveDetailsViewModel(item.Info, _instance!), this.TryGetHostId(),
            new OverlayDialogOptions
            {
                Mode = DialogMode.None,
                Buttons = DialogButton.None,
                CanLightDismiss = false,
                CanResize = false,
                IsCloseButtonVisible = true,
                CloseBtnMargin = new Thickness(0, 12, 12, 0)
            });
    }
}

public sealed class SaveItem(WorldSaveInfo info, MinecraftInstance? instance = null)
{
    private long? _folderSize;
    public WorldSaveInfo Info { get; } = info;
    public bool CanQuickEnter => instance?.MinecraftEntry is { } entry && entry.ReleaseTime > new DateTime(2023, 4, 4);
    public string FolderName => Info.FolderName;
    public string DisplayName => string.IsNullOrWhiteSpace(Info.LevelName) ? Info.FolderName : Info.LevelName;

    public string FolderNameSuffix => string.Equals(DisplayName, FolderName, StringComparison.Ordinal)
        ? string.Empty
        : $"{FolderName}";

    public long FolderSize => _folderSize ??= ResourceListUi.GetFolderSize(Info.FolderPath);
    public string? IconPath => Info.IconPath;
    public bool HasIcon => IconPath != null;
    public IAsyncImageLoader ImageLoader { get; } = new LocalImageLoader(112);

    public string Summary
    {
        get
        {
            var summary = string.Format(CommonLanguageManager.Instance.saves_summary.CurrentValue(),
                Info.Version ?? CommonLanguageManager.Instance.recentPlay_unknownVersion.CurrentValue(),
                GetGameModeText(Info.GameMode));
            summary = $"{ResourceListUi.FormatSize(FolderSize)}·{summary}";
            if (Info.AllowCommands == true)
                summary += CommonLanguageManager.Instance.saves_allowCheats.CurrentValue();
            if (Info.IsLocked)
                summary += CommonLanguageManager.Instance.saves_locked.CurrentValue();
            return summary;
        }
    }

    public string LastPlayedText =>
        string.Format(CommonLanguageManager.Instance.saves_lastPlayed.CurrentValue(),
            Info.LastPlayedTime ?? Info.LastWriteTime);

    public string Details
    {
        get
        {
            var unknown = CommonLanguageManager.Instance.account_unknown.CurrentValue();
            var lastPlayed = Info.LastPlayedTime?.ToString("yyyy-MM-dd HH:mm") ?? unknown;
            var allowCheats = Info.AllowCommands is null
                ? unknown
                : Info.AllowCommands.Value
                    ? CommonLanguageManager.Instance.common_yes.CurrentValue()
                    : CommonLanguageManager.Instance.common_no.CurrentValue();
            return string.Format(CommonLanguageManager.Instance.saves_details.CurrentValue(), Info.FolderName,
                Info.CreationTime, Info.LastWriteTime, lastPlayed, Info.Version ?? unknown,
                Info.Seed?.ToString() ?? unknown, GetGameModeText(Info.GameMode), allowCheats,
                Info.PlayerDataCount, Info.DataPackArchiveCount);
        }
    }

    private static string GetGameModeText(int? gameMode)
    {
        return gameMode switch
        {
            0 => CommonLanguageManager.Instance.recentPlay_gameModeSurvival.CurrentValue(),
            1 => CommonLanguageManager.Instance.recentPlay_gameModeCreative.CurrentValue(),
            2 => CommonLanguageManager.Instance.recentPlay_gameModeAdventure.CurrentValue(),
            3 => CommonLanguageManager.Instance.recentPlay_gameModeSpectator.CurrentValue(),
            _ => CommonLanguageManager.Instance.recentPlay_gameModeUnknown.CurrentValue()
        };
    }
}

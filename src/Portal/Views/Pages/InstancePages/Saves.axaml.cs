using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AsyncImageLoader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using Portal.Views.StaticPages;
using SkiaSharp;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class Saves : UserControl, INotifyPropertyChanged
{
    private readonly MinecraftInstance? _instance;
    private readonly string? _savesPath;
    private readonly WorldSaveService _saveService = new();
    private readonly DispatcherTimer _lockRefreshTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _hasLoaded;
    private bool _isLoading;
    private bool _isRefreshingLockStates;
    private bool _isAttached;
    private string _filter = string.Empty;

    public ObservableCollection<SaveItem> Items { get; } = [];
    public ObservableCollection<SaveItem> FilteredItems { get; } = [];

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
    public string SaveCountText => IsLoading ? string.Empty : $"{FilteredItems.Count} 个";

    public Saves()
    {
        InitializeComponent();
        DataContext = this;
        _lockRefreshTimer.Tick += async (_, _) => await RefreshLockStatesAsync();
    }

    public Saves(MinecraftInstance instance) : this()
    {
        _instance = instance;
        _savesPath = instance.GetSpecialFolder(MinecraftSpecialFolder.SavesFolder);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        _ = LoadAsync();
        UpdateLockRefreshTimer();
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
        if (change.Property == Visual.IsVisibleProperty)
            UpdateLockRefreshTimer();
    }

    private async Task LoadAsync()
    {
        if (_hasLoaded || _instance == null)
            return;

        _hasLoaded = true;
        IsLoading = true;
        RaiseListProperties();
        var saves = await _saveService.ScanAsync(_instance);
        Items.Clear();
        foreach (var save in saves)
            Items.Add(new SaveItem(save));
        ApplyFilter();
        IsLoading = false;
        RaiseListProperties();
        await RefreshLockStatesAsync();
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(_filter)
            ? Items
            : Items.Where(item => item.FolderName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                                  item.DisplayName.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        FilteredItems.Clear();
        foreach (var item in filtered)
            FilteredItems.Add(item);
        RaiseListProperties();
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_savesPath))
            await TopLevel.GetTopLevel(this).Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(_savesPath));
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
            Title = "选择世界图标",
            AllowMultiple = false,
            FileTypeFilter =
                [new FilePickerFileType("图片") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"] }]
        });
        if (files.Count == 0)
            return;

        var iconPath = Path.Combine(item.Info.FolderPath, "icon.png");
        var temporaryIconPath = Path.Combine(item.Info.FolderPath, $".{Guid.NewGuid():N}.png");
        try
        {
            await using var input = await files[0].OpenReadAsync();
            using var image = SKBitmap.Decode(input) ?? throw new InvalidDataException("无法读取所选图片。");
            var cropSize = Math.Min(image.Width, image.Height);
            var source = new SKRectI((image.Width - cropSize) / 2, (image.Height - cropSize) / 2,
                (image.Width + cropSize) / 2, (image.Height + cropSize) / 2);
            using var surface = SKSurface.Create(new SKImageInfo(64, 64)) ?? throw new InvalidOperationException("无法创建图标。");
            surface.Canvas.DrawBitmap(image, source, new SKRect(0, 0, 64, 64), new SKSamplingOptions());
            using var png = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
            await using (var output = File.Create(temporaryIconPath))
                png.SaveTo(output);

            File.Move(temporaryIconPath, iconPath, true);
            RefreshItem(item, item.Info with { IconPath = iconPath });
            await RefreshSavesAsync();
            ShowNotice("世界图标已更换", NotificationType.Success);
        }
        catch (IOException ex) when (IsFileLocked(ex))
        {
            await ShowLockedAsync();
        }
        catch (IOException ex)
        {
            ShowNotice($"更换世界图标失败：{ex.Message}", NotificationType.Error);
        }
        catch (UnauthorizedAccessException)
        {
            ShowNotice("没有更换此世界图标的权限。", NotificationType.Error);
        }
        catch (Exception ex)
        {
            ShowNotice($"更换世界图标失败：{ex.Message}", NotificationType.Error);
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
                // The temporary file can only remain when another process locked it.
            }
            catch (UnauthorizedAccessException)
            {
                // The temporary file can only remain when another process locked it.
            }
        }
    }

    private async void ShowInfo_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item)
            return;
        await OverlayDialog.ShowStandardAsync(
            new TextBlock
                { Margin = new Avalonia.Thickness(24), Text = item.Details, TextWrapping = TextWrapping.Wrap }, null,
            this.TryGetHostId(),
            new OverlayDialogOptions
            {
                Title = item.DisplayName, Mode = DialogMode.None, Buttons = DialogButton.OK, CanLightDismiss = true
            });
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
                Margin = new Avalonia.Thickness(24), Text = $"确定要永久删除存档“{item.DisplayName}”吗？此操作无法撤销。",
                TextWrapping = TextWrapping.Wrap
            },
            null, this.TryGetHostId(), CreateDeleteConfirmationOptions("删除存档"));
        if (result != DialogResult.Yes)
            return;
        try
        {
            Directory.Delete(item.Info.FolderPath, true);
            Items.Remove(item);
            ApplyFilter();
            ShowNotice("存档已删除", NotificationType.Success);
        }
        catch (IOException ex) when (IsFileLocked(ex))
        {
            await ShowLockedAsync();
        }
        catch (IOException ex)
        {
            ShowNotice($"无法删除存档：{ex.Message}", NotificationType.Error);
        }
        catch (UnauthorizedAccessException)
        {
            ShowNotice("没有删除此存档的权限。", NotificationType.Error);
        }
    }

    private Task ShowLockedAsync()
    {
        ShowNotice("世界被Minecraft实例锁定", NotificationType.Warning);
        return Task.CompletedTask;
    }

    private void ShowNotice(string message, NotificationType type)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
            NotificationGateway.Notice(topLevel, message, type);
    }

    private static OverlayDialogOptions CreateDeleteConfirmationOptions(string title) => new()
    {
        Title = title,
        Mode = DialogMode.Error,
        Buttons = DialogButton.YesNo,
        OverrideYesButtonText = "删除",
        OverrideNoButtonText = "取消",
        CanLightDismiss = false,
        CanResize = false
    };

    private static SaveItem? GetItem(object? sender) => (sender as Control)?.Tag as SaveItem;

    private static bool IsFileLocked(IOException exception) => (exception.HResult & 0xffff) is 32 or 33;

    private async Task RefreshLockStatesAsync()
    {
        if (_isRefreshingLockStates || !IsVisible || Items.Count == 0)
            return;

        _isRefreshingLockStates = true;
        try
        {
            var items = Items.ToArray();
            var lockStates = await Task.WhenAll(items.Select(async item =>
                (Item: item, IsLocked: await _saveService.IsWorldLockedAsync(item.Info.FolderPath))));
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

        Items[index] = new SaveItem(info);
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

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void RaisePropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
    }
}

public sealed class SaveItem(WorldSaveInfo info)
{
    public WorldSaveInfo Info { get; } = info;
    public string FolderName => Info.FolderName;
    public string DisplayName => string.IsNullOrWhiteSpace(Info.LevelName) ? Info.FolderName : Info.LevelName;
    public string FolderNameSuffix => string.Equals(DisplayName, FolderName, StringComparison.Ordinal) ? string.Empty :
        $"{FolderName}";
    public string? IconPath => Info.IconPath;
    public bool HasIcon => IconPath != null;
    public IAsyncImageLoader ImageLoader { get; } = new SaveImageLoader();
    public string Summary => $"{Info.Version ?? "未知版本"}·{GetGameModeText(Info.GameMode)}" +
                             (Info.AllowCommands == true ? "·允许作弊" : string.Empty) +
                             (Info.IsLocked ? "·锁定中" : string.Empty);
    public string LastPlayedText => $"最近游玩：{(Info.LastPlayedTime ?? Info.LastWriteTime):yyyy-MM-dd HH:mm}";

    public string Details =>
        $"文件夹：{Info.FolderName}\n创建时间：{Info.CreationTime:yyyy-MM-dd HH:mm}\n修改时间：{Info.LastWriteTime:yyyy-MM-dd HH:mm}\n最近游玩：{(Info.LastPlayedTime?.ToString("yyyy-MM-dd HH:mm") ?? "未知")}\n版本：{Info.Version ?? "未知"}\n种子：{(Info.Seed?.ToString() ?? "未知")}\n游戏模式：{GetGameModeText(Info.GameMode)}\n允许作弊：{(Info.AllowCommands is null ? "未知" : Info.AllowCommands.Value ? "是" : "否")}\n玩家数据：{Info.PlayerDataCount}\n数据包：{Info.DataPackArchiveCount}";

    private static string GetGameModeText(int? gameMode) =>
        gameMode switch { 0 => "生存", 1 => "创造", 2 => "冒险", 3 => "旁观", _ => "未知模式" };
}

public sealed class SaveImageLoader : IAsyncImageLoader
{
    public Task<Bitmap?> ProvideImageAsync(string url) => Task.Run<Bitmap?>(() =>
    {
        try
        {
            using var stream = File.OpenRead(url);
            return Bitmap.DecodeToWidth(stream, 112);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    });

    public void Dispose()
    {
    }
}

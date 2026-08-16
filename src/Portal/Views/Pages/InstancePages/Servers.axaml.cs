using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using Portal.Module.DesktopShortcut;
using Portal.Services;
using Portal.Views.Pages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class Servers : UserControl, INotifyPropertyChanged, IDisposable
{
    private const int PingConcurrency = 5;
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly MinecraftInstance? _instance;
    private readonly MinecraftServerPingService _pingService = new();
    private readonly SemaphoreSlim _pingSemaphore = new(PingConcurrency);
    private readonly DispatcherTimer _autoRefreshTimer = new() { Interval = AutoRefreshInterval };
    private readonly CancellationTokenSource _disposeCancellation = new();
    private CancellationTokenSource? _sessionCancellation;
    private bool _hasLoaded;
    private bool _isLoading;
    private int _isRefreshing;
    private bool _isDisposed;
    private event PropertyChangedEventHandler? ServerPropertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => ServerPropertyChanged += value;
        remove => ServerPropertyChanged -= value;
    }

    public ObservableCollection<ServerItem> Items { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            RaisePropertyChanged(nameof(IsLoading));
            RaisePropertyChanged(nameof(IsEmpty));
            RaisePropertyChanged(nameof(ServerCountText));
        }
    }

    public bool IsEmpty => !IsLoading && Items.Count == 0;
    public string ServerCountText => IsLoading ? string.Empty : $"{Items.Count} 个";

    public bool IsRefreshing
    {
        get => _isRefreshing > 0;
        private set
        {
            var next = value ? 1 : 0;
            if (Interlocked.Exchange(ref _isRefreshing, next) == next) return;
            RaisePropertyChanged(nameof(IsRefreshing));
        }
    }

    public Servers()
    {
        InitializeComponent();
        DataContext = this;
        _autoRefreshTimer.Tick += AutoRefreshTimer_OnTick;
    }

    public Servers(MinecraftInstance instance) : this()
    {
        _instance = instance;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ = RefreshOnAttachAsync();
        _autoRefreshTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _autoRefreshTimer.Stop();
        CancelSession();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == Visual.IsVisibleProperty)
        {
            if (IsVisible)
                _autoRefreshTimer.Start();
            else
                _autoRefreshTimer.Stop();
        }
    }

    private async Task LoadAsync()
    {
        if (_hasLoaded || _instance == null)
            return;

        _hasLoaded = true;
        await ReloadAsync();
    }

        private async Task RefreshOnAttachAsync()
    {
        if (_instance == null)
            return;

        if (_hasLoaded)
            await PingAllAsync();
        else
            await LoadAsync();
    }

    private async Task ReloadAsync()
    {
        if (_instance == null)
            return;

        IsLoading = true;
        RaiseListProperties();
        var entries = await Task.Run(() => JavaServerManager.Read(_instance));
        Items.Clear();
        foreach (var entry in entries)
            Items.Add(new ServerItem(entry));
        IsLoading = false;
        RaiseListProperties();
        await PingAllAsync();
    }

    private async void AutoRefreshTimer_OnTick(object? sender, EventArgs e)
    {
        
        if (!IsVisible || _isRefreshing != 0)
            return;

        await PingAllAsync();
    }

        private async Task RefreshAllAsync()
    {
        await ReloadAsync();
        Notify(_instance == null ? "刷新失败" : "服务器状态已刷新", NotificationType.Success);
    }

    private async Task PingAllAsync()
    {
        if (_instance == null)
            return;

        if (Interlocked.CompareExchange(ref _isRefreshing, 1, 0) != 0)
            return;
        RaisePropertyChanged(nameof(IsRefreshing));

        _sessionCancellation?.Cancel();
        var session = new CancellationTokenSource();
        _sessionCancellation = session;

        try
        {
            var snapshot = Items.ToArray();
            var tasks = snapshot.Select(item => PingOneAsync(item, session.Token));
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Logger.Warning($"批量检测服务器状态失败。{Environment.NewLine}{exception}");
        }
        finally
        {
            if (ReferenceEquals(_sessionCancellation, session))
            {
                _sessionCancellation = null;
                session.Dispose();
            }

            Interlocked.Exchange(ref _isRefreshing, 0);
            RaisePropertyChanged(nameof(IsRefreshing));
        }
    }

    private async Task PingOneAsync(ServerItem item, CancellationToken cancellationToken)
    {
        await Dispatcher.UIThread.InvokeAsync(() => item.ApplyPinging());
        await _pingSemaphore.WaitAsync(cancellationToken);
        try
        {
            var status = await _pingService.PingAsync(item.Entry.Address, cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() => item.ApplyStatus(status));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Logger.Warning($"检测服务器状态失败：{item.Entry.Address}{Environment.NewLine}{exception}");
            await Dispatcher.UIThread.InvokeAsync(() => item.ApplyStatus(null));
        }
        finally
        {
            _pingSemaphore.Release();
        }
    }

    private void CancelSession()
    {
        _sessionCancellation?.Cancel();
        _sessionCancellation = null;
    }

    private async void Refresh_OnClick(object? sender, RoutedEventArgs e)
    {
        await RefreshAllAsync();
    }

    private async void AddServer_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_instance == null)
            return;

        var result = await ServerEditDialogHelper.ShowAsync("添加服务器", string.Empty, string.Empty,
            this.TryGetHostId());
        if (result == null)
            return;

        if (JavaServerManager.Add(_instance, result.Name, result.Address))
        {
            Notify($"服务器“{result.Name}”已添加", NotificationType.Success);
            await ReloadAsync();
        }
        else
        {
            Notify("添加服务器失败", NotificationType.Error);
        }
    }

    private async void EditServer_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_instance == null || (sender as Control)?.Tag is not ServerItem item)
            return;

        var index = Items.IndexOf(item);
        if (index < 0)
            return;

        var result = await ServerEditDialogHelper.ShowAsync("编辑服务器", item.Name, item.Entry.Address,
            this.TryGetHostId());
        if (result == null)
            return;

        if (JavaServerManager.Update(_instance, index, result.Name, result.Address))
        {
            Notify("服务器已更新", NotificationType.Success);
            await ReloadAsync();
        }
        else
        {
            Notify("编辑服务器失败", NotificationType.Error);
        }
    }

    private async void RemoveServer_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_instance == null || (sender as Control)?.Tag is not ServerItem item)
            return;

        var index = Items.IndexOf(item);
        if (index < 0)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var result = await OverlayDialog.ShowStandardAsync(
            new TextBlock
            {
                Margin = new Thickness(24),
                Text = $"确定要从服务器列表中删除“{item.Name}”吗？此操作不会影响已经连接的存档。",
                TextWrapping = TextWrapping.Wrap
            },
            null, this.TryGetHostId(), new OverlayDialogOptions
            {
                Title = "删除服务器",
                Mode = DialogMode.Error,
                Buttons = DialogButton.YesNo,
                OverrideYesButtonText = "删除",
                OverrideNoButtonText = "取消",
                CanLightDismiss = false,
                CanResize = false
            });
        if (result != DialogResult.Yes)
            return;

        if (JavaServerManager.Remove(_instance, index))
        {
            Notify($"服务器“{item.Name}”已删除", NotificationType.Success);
            await ReloadAsync();
        }
        else
        {
            Notify("删除服务器失败", NotificationType.Error);
        }
    }

    private async void RefreshServer_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not ServerItem item || _isRefreshing != 0)
            return;

        IsRefreshing = true;
        try
        {
            using var session = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellation.Token);
            await PingOneAsync(item, session.Token);
            Notify($"“{item.Name}”状态已刷新", NotificationType.Success);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void LaunchServer_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is ServerItem item)
            LaunchServer(item);
    }

    private void ServerCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is Visual visual && (visual is Button || visual.FindAncestorOfType<Button>() != null))
            return;

        if (sender is Control { DataContext: ServerItem item })
            LaunchServer(item);
    }

    private void LaunchServer(ServerItem item)
    {
        if (_instance == null || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        _ = MinecraftLaunchService.LaunchAsync(_instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(_instance,
                logSession => MinecraftLogPage.Open(logSession, topLevel)), BuildTarget(item));
    }

    private RecentPlayTarget BuildTarget(ServerItem item)
    {
        var entry = item.Entry;
        return new RecentPlayTarget(
            _instance!,
            RecentPlayTargetType.Server,
            $"{entry.Host}:{entry.Port}",
            entry.Name,
            $"服务器·{entry.DisplayAddress}",
            DateTime.Now,
            ServerIconData: entry.IconData,
            ServerAddress: entry.Host,
            ServerPort: entry.Port);
    }

    private async void CreateShortcut_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_instance == null || (sender as Control)?.Tag is not ServerItem item)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        await DesktopShortcutUi.CreateAsync(topLevel,
            () => DesktopShortcutService.CreateAsync(_instance, BuildTarget(item)));
    }

    private async void CopyAddress_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not ServerItem item)
            return;

        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(item.Entry.DisplayAddress);

        Notify($"已复制地址 {item.Entry.DisplayAddress}", NotificationType.Success);
    }

    private void Title_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_instance == null || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var directory = Path.GetDirectoryName(JavaServerManager.GetServersDatPath(_instance));
        if (directory != null && Directory.Exists(directory))
            _ = topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(directory));
    }

    private void RaiseListProperties()
    {
        RaisePropertyChanged(nameof(IsEmpty));
        RaisePropertyChanged(nameof(ServerCountText));
    }

    private void RaisePropertyChanged(string propertyName) =>
        ServerPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static void Notify(string message, NotificationType type)
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes
            .IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: { } window
            })
            NotificationGateway.Notice(window, message, type);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _autoRefreshTimer.Stop();
        _autoRefreshTimer.Tick -= AutoRefreshTimer_OnTick;
        _disposeCancellation.Cancel();
        CancelSession();
        _disposeCancellation.Dispose();
        foreach (var item in Items)
            item.Dispose();
        Items.Clear();
        DataContext = null;
    }
}

public enum ServerItemStatus
{
    Unknown,
    Pinging,
    Online,
    Offline
}

public sealed partial class ServerItem : ObservableObject, IDisposable
{
    private static readonly IBrush OnlineBrush = new SolidColorBrush(Color.Parse("#52C41A"));
    private static readonly IBrush OfflineBrush = new SolidColorBrush(Color.Parse("#F5222D"));
    private static readonly IBrush PendingBrush = new SolidColorBrush(Color.Parse("#8C8C8C"));
    private static readonly IBrush PingGoodBrush = new SolidColorBrush(Color.Parse("#52C41A"));
    private static readonly IBrush PingFairBrush = new SolidColorBrush(Color.Parse("#FAAD14"));
    private static readonly IBrush PingPoorBrush = new SolidColorBrush(Color.Parse("#F5222D"));

    private Bitmap? _ownedIcon;

    public MinecraftServerEntry Entry { get; }

    public string Name => Entry.Name;
    public string AddressText => Entry.DisplayAddress;

    [ObservableProperty]
    public partial Bitmap? Icon { get; set; }

    [ObservableProperty]
    public partial bool HasIcon { get; set; }

    [ObservableProperty]
    public partial ServerItemStatus Status { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "未检测";

    [ObservableProperty]
    public partial IBrush StatusBrush { get; set; } = PendingBrush;

    [ObservableProperty]
    public partial string PingText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasPing { get; set; }

    [ObservableProperty]
    public partial IBrush PingBrush { get; set; } = PingGoodBrush;

    [ObservableProperty]
    public partial string PlayersText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasPlayers { get; set; }

    [ObservableProperty]
    public partial string VersionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasVersion { get; set; }

    [ObservableProperty]
    public partial string DescriptionText { get; set; } = "等待检测...";

    public ServerItem(MinecraftServerEntry entry)
    {
        Entry = entry;
        SetIcon(DecodeBitmap(entry.IconData));
    }

    public void ApplyPinging()
    {
        Status = ServerItemStatus.Pinging;
        StatusText = "检测中";
        StatusBrush = PendingBrush;
    }

    public void ApplyStatus(MinecraftServerStatus? status)
    {
        if (status == null)
        {
            Status = ServerItemStatus.Offline;
            StatusText = "无法连接";
            StatusBrush = OfflineBrush;
            HasPing = false;
            HasPlayers = false;
            HasVersion = false;
            DescriptionText = "无法连接到服务器，请检查地址或稍后再试";
            return;
        }

        Status = ServerItemStatus.Online;
        StatusText = "在线";
        StatusBrush = OnlineBrush;

        PingText = $"{status.Latency} ms";
        HasPing = true;
        PingBrush = status.Latency < 100 ? PingGoodBrush
            : status.Latency < 300 ? PingFairBrush
            : PingPoorBrush;

        var hasPlayerCount = status.MaxPlayers > 0 || status.OnlinePlayers > 0;
        PlayersText = hasPlayerCount ? $"{status.OnlinePlayers} / {status.MaxPlayers} 人" : string.Empty;
        HasPlayers = hasPlayerCount;

        VersionText = string.IsNullOrWhiteSpace(status.Version) ? string.Empty : status.Version;
        HasVersion = !string.IsNullOrWhiteSpace(status.Version);

        DescriptionText = string.IsNullOrWhiteSpace(status.Description)
            ? "暂无描述"
            : status.Description;

        if (!string.IsNullOrWhiteSpace(status.Favicon))
        {
            var favicon = DecodeFavicon(status.Favicon);
            if (favicon != null)
                SetIcon(favicon);
        }
    }

    private void SetIcon(Bitmap? bitmap)
    {
        var previous = _ownedIcon;
        _ownedIcon = bitmap;
        Icon = bitmap;
        HasIcon = bitmap != null;
        previous?.Dispose();
    }

    private static Bitmap? DecodeFavicon(string favicon)
    {
        var comma = favicon.IndexOf(',');
        if (comma < 0)
            return null;

        try
        {
            return DecodeBitmap(Convert.FromBase64String(favicon[(comma + 1)..]));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Bitmap? DecodeBitmap(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data);
            return Bitmap.DecodeToWidth(stream, 64);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        var icon = _ownedIcon;
        _ownedIcon = null;
        Icon = null;
        HasIcon = false;
        if (icon != null)
            Dispatcher.UIThread.Post(icon.Dispose, DispatcherPriority.Background);
    }
}

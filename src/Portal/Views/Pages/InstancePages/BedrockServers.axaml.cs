using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance.Bedrock;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class BedrockServers : UserControl, INotifyPropertyChanged, IDisposable
{
    private const int PingConcurrency = 5;
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(30);
    private readonly DispatcherTimer _autoRefreshTimer = new() { Interval = AutoRefreshInterval };
    private readonly CancellationTokenSource _disposeCancellation = new();

    private readonly MinecraftInstance? _instance;
    private readonly SemaphoreSlim _pingSemaphore = new(PingConcurrency);
    private readonly BedrockServerPingService _pingService = new();
    private string _filter = string.Empty;
    private bool _isLoading, _isDisposed;
    private int _isRefreshing;
    private int _loadSequence;
    private CancellationTokenSource? _sessionCancellation;

    public BedrockServers()
    {
        InitializeComponent();
        DataContext = this;
        _autoRefreshTimer.Tick += AutoRefreshTimer_OnTick;
    }

    public BedrockServers(MinecraftInstance instance) : this()
    {
        _instance = instance;
    }

    public ObservableCollection<string> UserIds { get; } = [];
    public ObservableCollection<BedrockServerItem> Items { get; } = [];
    public ObservableCollection<BedrockServerItem> FilteredItems { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            RaisePropertyChanged(nameof(IsLoading));
            RaisePropertyChanged(nameof(IsEmpty));
            RaisePropertyChanged(nameof(CountText));
        }
    }

    public bool IsEmpty => !IsLoading && FilteredItems.Count == 0;
    public string CountText => IsLoading
        ? string.Empty
        : string.Format(CommonLanguageManager.Instance.resourceList_count.CurrentValue(), FilteredItems.Count);

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
        FilteredItems.Clear();
        DataContext = null;
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => ServerPropertyChanged += value;
        remove => ServerPropertyChanged -= value;
    }

    private event PropertyChangedEventHandler? ServerPropertyChanged;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshUserIds();
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
        if (change.Property == IsVisibleProperty)
        {
            if (IsVisible)
                _autoRefreshTimer.Start();
            else
                _autoRefreshTimer.Stop();
        }
    }

    private async void AutoRefreshTimer_OnTick(object? sender, EventArgs e)
    {
        if (!IsVisible || _isRefreshing != 0)
            return;

        await PingAllAsync();
    }

    private void RefreshUserIds()
    {
        if (_instance?.BedrockConfig is not { } config)
            return;

        var selectedUserId = UserIdSelector.SelectedItem as string;
        var userIds = BedrockDataPathResolver.GetWorldUserIds(config);
        UserIds.Clear();
        foreach (var userId in userIds)
            UserIds.Add(userId);

        UserIdSelector.SelectedItem = selectedUserId != null && UserIds.Contains(selectedUserId)
            ? selectedUserId
            : UserIds.FirstOrDefault(userId => !string.Equals(userId, "Shared", StringComparison.OrdinalIgnoreCase))
              ?? UserIds.FirstOrDefault();
    }

    private void UserIdSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_instance?.BedrockConfig is not { } config || _isDisposed)
            return;

        var sequence = ++_loadSequence;
        IsLoading = true;
        RaiseListProperties();
        try
        {
            var userId = GetSelectedUserId();
            var entries = await Task.Run(() => BedrockServerManager.Read(config, userId),
                _disposeCancellation.Token);
            if (_isDisposed || sequence != _loadSequence)
                return;

            foreach (var item in Items)
                item.Dispose();
            Items.Clear();
            foreach (var entry in entries)
                Items.Add(new BedrockServerItem(entry));
            ApplyFilter();
        }
        finally
        {
            if (!_isDisposed && sequence == _loadSequence)
            {
                IsLoading = false;
                RaiseListProperties();
            }
        }

        await PingAllAsync();
    }

    private async Task ReloadAsyncSilently()
    {
        await LoadAsync();
    }

    private async void RefreshAll_OnClick(object? sender, RoutedEventArgs e)
    {
        await ReloadAsyncSilently();
        Notify(_instance == null
                ? CommonLanguageManager.Instance.bedrockServers_refreshFailed.CurrentValue()
                : CommonLanguageManager.Instance.bedrockServers_statusRefreshed.CurrentValue(),
            NotificationType.Success);
    }

    private async Task PingAllAsync()
    {
        if (_instance == null || _isDisposed)
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
            Logger.Warning(string.Format(LogLanguageManager.Instance.bedrockServers_batchPingFailed.CurrentValue(),
                Environment.NewLine, exception));
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

    private async Task PingOneAsync(BedrockServerItem item, CancellationToken cancellationToken)
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
            Logger.Warning(string.Format(LogLanguageManager.Instance.bedrockServers_pingFailed.CurrentValue(),
                item.Entry.Address, Environment.NewLine, exception));
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

    private async void AddServer_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_instance?.BedrockConfig is not { } config)
            return;

        var result = await ServerEditDialogHelper.ShowAsync(
            CommonLanguageManager.Instance.bedrockServers_addServer.CurrentValue(), string.Empty, string.Empty,
            this.TryGetHostId(), BedrockServerManager.DefaultPort);
        if (result == null)
            return;

        if (BedrockServerManager.Add(config, GetSelectedUserId(), result.Name, result.Address))
        {
            Notify(string.Format(CommonLanguageManager.Instance.bedrockServers_serverAdded.CurrentValue(),
                result.Name), NotificationType.Success);
            await ReloadAsyncSilently();
        }
        else
        {
            Notify(CommonLanguageManager.Instance.bedrockServers_addFailed.CurrentValue(), NotificationType.Error);
        }
    }

    private async void EditServer_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_instance?.BedrockConfig is not { } config || (sender as Control)?.Tag is not BedrockServerItem item)
            return;

        var result = await ServerEditDialogHelper.ShowAsync(
            CommonLanguageManager.Instance.bedrockServers_editServer.CurrentValue(), item.Name, item.Entry.Address,
            this.TryGetHostId(), BedrockServerManager.DefaultPort);
        if (result == null)
            return;

        if (BedrockServerManager.Update(config, GetSelectedUserId(), item.Entry.LineIndex,
                result.Name, result.Address))
        {
            Notify(CommonLanguageManager.Instance.bedrockServers_serverUpdated.CurrentValue(), NotificationType.Success);
            await ReloadAsyncSilently();
        }
        else
        {
            Notify(CommonLanguageManager.Instance.bedrockServers_editFailed.CurrentValue(), NotificationType.Error);
        }
    }

    private async void RemoveServer_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_instance?.BedrockConfig is not { } config || (sender as Control)?.Tag is not BedrockServerItem item)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var result = await OverlayDialog.ShowStandardAsync(
            new TextBlock
            {
                Margin = new Thickness(24),
                Text = string.Format(CommonLanguageManager.Instance.bedrockServers_deleteConfirm.CurrentValue(),
                    item.Name),
                TextWrapping = TextWrapping.Wrap
            },
            null, this.TryGetHostId(), new OverlayDialogOptions
            {
                Title = CommonLanguageManager.Instance.bedrockServers_deleteServer.CurrentValue(),
                Mode = DialogMode.Error,
                Buttons = DialogButton.YesNo,
                OverrideYesButtonText = CommonLanguageManager.Instance.dashboard_delete.CurrentValue(),
                OverrideNoButtonText = CommonLanguageManager.Instance.common_cancel.CurrentValue(),
                CanLightDismiss = false,
                CanResize = false
            });
        if (result != DialogResult.Yes)
            return;

        if (BedrockServerManager.Remove(config, GetSelectedUserId(), item.Entry.LineIndex))
        {
            Notify(string.Format(CommonLanguageManager.Instance.bedrockServers_serverDeleted.CurrentValue(),
                item.Name), NotificationType.Success);
            await ReloadAsyncSilently();
        }
        else
        {
            Notify(CommonLanguageManager.Instance.bedrockServers_deleteFailed.CurrentValue(), NotificationType.Error);
        }
    }

    private async void RefreshServer_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not BedrockServerItem item || _isRefreshing != 0)
            return;

        IsRefreshing = true;
        try
        {
            using var session = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellation.Token);
            await PingOneAsync(item, session.Token);
            Notify(string.Format(CommonLanguageManager.Instance.bedrockServers_statusRefreshedNamed.CurrentValue(),
                item.Name), NotificationType.Success);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async void CopyAddress_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not BedrockServerItem item)
            return;

        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(item.Entry.CopyAddress);

        Notify(string.Format(CommonLanguageManager.Instance.bedrockServers_addressCopied.CurrentValue(),
            item.Entry.CopyAddress), NotificationType.Success);
    }

    private void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_instance?.BedrockConfig is not { } config || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var directory = Path.GetDirectoryName(BedrockServerManager.GetExternalServersPath(config,
            GetSelectedUserId()));
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
            _ = topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(directory));
        }
    }

    private void SearchBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        _filter = (sender as TextBox)?.Text ?? string.Empty;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(_filter)
            ? Items
            : Items.Where(item =>
                item.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                item.Entry.DisplayAddress.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        FilteredItems.Clear();
        foreach (var item in filtered)
            FilteredItems.Add(item);
        RaiseListProperties();
    }

    private void RaiseListProperties()
    {
        RaisePropertyChanged(nameof(IsEmpty));
        RaisePropertyChanged(nameof(CountText));
    }

    private void RaisePropertyChanged(string propertyName)
    {
        ServerPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string GetSelectedUserId()
    {
        return UserIdSelector.SelectedItem as string ?? "Shared";
    }

    private static void Notify(string message, NotificationType type)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: { } window
            })
            window.Notice(message, type);
    }
}

public enum BedrockServerItemStatus
{
    Unknown,
    Pinging,
    Online,
    Offline
}

public sealed partial class BedrockServerItem : ObservableObject, IDisposable
{
    private static readonly IBrush OnlineBrush = new SolidColorBrush(Color.Parse("#52C41A"));
    private static readonly IBrush OfflineBrush = new SolidColorBrush(Color.Parse("#F5222D"));
    private static readonly IBrush PendingBrush = new SolidColorBrush(Color.Parse("#8C8C8C"));
    private static readonly IBrush PingGoodBrush = new SolidColorBrush(Color.Parse("#52C41A"));
    private static readonly IBrush PingFairBrush = new SolidColorBrush(Color.Parse("#FAAD14"));
    private static readonly IBrush PingPoorBrush = new SolidColorBrush(Color.Parse("#F5222D"));

    private Bitmap? _ownedIcon;

    public BedrockServerItem(BedrockServerEntry entry)
    {
        Entry = entry;
        SetIcon(DecodeBitmap(entry.IconData));
    }

    public BedrockServerEntry Entry { get; }

    public string Name => Entry.Name;
    public string AddressText => Entry.DisplayAddress;

    [ObservableProperty] public partial Bitmap? Icon { get; set; }

    [ObservableProperty] public partial bool HasIcon { get; set; }

    [ObservableProperty] public partial BedrockServerItemStatus Status { get; set; }

    [ObservableProperty] public partial string StatusText { get; set; } =
        CommonLanguageManager.Instance.bedrockServers_statusUnknown.CurrentValue();

    [ObservableProperty] public partial IBrush StatusBrush { get; set; } = PendingBrush;

    [ObservableProperty] public partial string PingText { get; set; } = string.Empty;

    [ObservableProperty] public partial bool HasPing { get; set; }

    [ObservableProperty] public partial IBrush PingBrush { get; set; } = PingGoodBrush;

    [ObservableProperty] public partial string PlayersText { get; set; } = string.Empty;

    [ObservableProperty] public partial bool HasPlayers { get; set; }

    [ObservableProperty] public partial string VersionText { get; set; } = string.Empty;

    [ObservableProperty] public partial bool HasVersion { get; set; }

    [ObservableProperty] public partial string DescriptionText { get; set; } =
        CommonLanguageManager.Instance.bedrockServers_waitingDetection.CurrentValue();

    public void Dispose()
    {
        var icon = _ownedIcon;
        _ownedIcon = null;
        Icon = null;
        HasIcon = false;
        if (icon != null)
            Dispatcher.UIThread.Post(icon.Dispose, DispatcherPriority.Background);
    }

    public void ApplyPinging()
    {
        Status = BedrockServerItemStatus.Pinging;
        StatusText = CommonLanguageManager.Instance.bedrockServers_pinging.CurrentValue();
        StatusBrush = PendingBrush;
    }

    public void ApplyStatus(BedrockServerStatus? status)
    {
        if (status == null)
        {
            Status = BedrockServerItemStatus.Offline;
            StatusText = CommonLanguageManager.Instance.bedrockServers_cannotConnect.CurrentValue();
            StatusBrush = OfflineBrush;
            HasPing = false;
            HasPlayers = false;
            HasVersion = false;
            DescriptionText = CommonLanguageManager.Instance.bedrockServers_cannotConnectDescription.CurrentValue();
            return;
        }

        Status = BedrockServerItemStatus.Online;
        StatusText = CommonLanguageManager.Instance.bedrockServers_online.CurrentValue();
        StatusBrush = OnlineBrush;

        PingText = $"{status.Latency} ms";
        HasPing = true;
        PingBrush = status.Latency < 100 ? PingGoodBrush
            : status.Latency < 300 ? PingFairBrush
            : PingPoorBrush;

        var hasPlayerCount = status.MaxPlayers > 0 || status.OnlinePlayers > 0;
        PlayersText = hasPlayerCount
            ? string.Format(CommonLanguageManager.Instance.bedrockServers_players.CurrentValue(),
                status.OnlinePlayers, status.MaxPlayers)
            : string.Empty;
        HasPlayers = hasPlayerCount;

        VersionText = string.IsNullOrWhiteSpace(status.Version) ? string.Empty : status.Version;
        HasVersion = !string.IsNullOrWhiteSpace(status.Version);

        DescriptionText = string.IsNullOrWhiteSpace(status.Motd)
            ? CommonLanguageManager.Instance.bedrockServers_noDescription.CurrentValue()
            : status.Motd;
    }

    private void SetIcon(Bitmap? bitmap)
    {
        var previous = _ownedIcon;
        _ownedIcon = bitmap;
        Icon = bitmap;
        HasIcon = bitmap != null;
        previous?.Dispose();
    }

    private static Bitmap? DecodeBitmap(byte[]? data)
    {
        if (data == null) return null;
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
}
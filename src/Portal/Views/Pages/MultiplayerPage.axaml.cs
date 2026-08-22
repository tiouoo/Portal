using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Const;
using Portal.Core.Module.AggregatedSearch;
using Portal.Core.Module.Multiplayer;
using Portal.Localization;
using Portal.Views.Components;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Gateway;
using Tio.Avalonia.Standard.Tab.Interface;

using Portal.Module;
namespace Portal.Views.Pages;

[AggregatedSearchPage("pages_multiplayer", "pages_multiplayerPath", "Multiplayer")]
public partial class MultiplayerPage : UserControl, ITioTabPage
{
    public MultiplayerPage() : this(MinecraftEdition.Java)
    {
    }

    public MultiplayerPage(MinecraftEdition edition = MinecraftEdition.Java)
    {
        InitializeComponent();
        PageInfo.Title = OperatingSystem.IsWindows()
            ? edition == MinecraftEdition.Java
                ? CommonLanguageManager.Instance.multiplayer_titleJava.CurrentValue()
                : CommonLanguageManager.Instance.multiplayer_titleBedrock.CurrentValue()
            : CommonLanguageManager.Instance.multiplayer_title.CurrentValue();
        ViewModel = new MultiplayerPageViewModel(edition);
        DataContext = ViewModel;
        Loaded += OnLoaded;
    }

    public MultiplayerPageViewModel ViewModel { get; }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = CommonLanguageManager.Instance.multiplayer_title.CurrentValue(),
        IconGlyph = "\ue614", IconFont = IconResources.FontFamilyName
    };

    public TabEntry HostTab { get; set; } = null!;

    public void OnClose()
    {
        Logger.Info($"[Multiplayer] Page closing for {ViewModel.Edition} edition.");
        Loaded -= OnLoaded;
        ViewModel.Deactivate();
        DataContext = null;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        Logger.Info($"[Multiplayer] Page loaded for {ViewModel.Edition} edition.");
        if (TopLevel.GetTopLevel(this) is OverlayWindow)
            this.FindControl<ContentControl>("Frame")!.MaxWidth = double.PositiveInfinity;

        ViewModel.Activate();
        ShowEditionContent();
        Loaded += (s, e) =>
        {
            var a = Frame.Content;
            Frame.Content = null;
            Frame.Content = a;
        };
        _ = ViewModel.InitializeAsync();
    }

    private void ShowEditionContent()
    {
        this.FindControl<ContentControl>("Frame")!.Content = new MultiplayerContentPage(ViewModel);
    }
}

public partial class MultiplayerPageViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly GravityConeClient SharedClient = new();
    private static readonly SemaphoreSlim SharedClientStartLock = new(1, 1);
    private static GravityConeInstallation? SharedInstallation;
    private readonly RoomState _bedrockRoom = new();
    private readonly RoomState _javaRoom = new();
    private readonly CancellationTokenSource _lifetime = new();
    private GravityConeClient? _client;
    private bool _disposed;
    private GravityConeInstallation? _installation;
    private bool _isActive;
    private int _isRefreshingRoomStatus;
    private DateTimeOffset _lastRoomStatusRequest = DateTimeOffset.MinValue;
    private CancellationTokenSource? _roomOperationCancellation;

    public MultiplayerPageViewModel(MinecraftEdition edition)
    {
        Edition = edition;
        PlayerName = string.IsNullOrWhiteSpace(Data.ConfigEntry.OnlinePlayerName)
            ? Data.ConfigEntry.UsingMinecraftMinecraftAccount?.Name ?? string.Empty
            : Data.ConfigEntry.OnlinePlayerName;
    }

    public MinecraftEdition Edition { get; } = MinecraftEdition.Java;

    public bool IsJava => Edition == MinecraftEdition.Java;
    public bool IsBedrock => Edition == MinecraftEdition.Bedrock;
    public string EditionTitle => CommonLanguageManager.Instance.multiplayer_title.CurrentValue();
    public bool IsNotBusy => !IsBusy;
    public bool CanOperate => IsReady && !IsBusy;
    public bool IsNotInRoom => !IsInRoom;
    public bool ShowJavaRoomActions => IsJava && IsNotInRoom;
    public bool ShowBedrockRoomActions => IsBedrock && IsNotInRoom;
    public bool CanCreateRoom => CanOperate && IsNotInRoom && (IsBedrock || ResolveJavaPort() is not null);
    public bool HasNatSummary => !string.IsNullOrWhiteSpace(NatSummary);
    public string MemberCountText =>
        string.Format(CommonLanguageManager.Instance.multiplayer_memberCount.CurrentValue(), Members.Count);
    public bool IsRoomOperationInProgress => IsCreatingRoom || IsJoiningRoom;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyPropertyChangedFor(nameof(CanOperate))]
    [NotifyPropertyChangedFor(nameof(CanCreateRoom))]
    [NotifyPropertyChangedFor(nameof(CanProbeNat))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyPropertyChangedFor(nameof(CanOperate))]
    [NotifyPropertyChangedFor(nameof(CanCreateRoom))]
    [NotifyPropertyChangedFor(nameof(CanProbeNat))]
    [NotifyPropertyChangedFor(nameof(IsComponentBannerVisible))]
    [NotifyPropertyChangedFor(nameof(ComponentTitle))]
    [NotifyPropertyChangedFor(nameof(InstallActionText))]
    public partial bool IsComponentMissing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyPropertyChangedFor(nameof(CanOperate))]
    [NotifyPropertyChangedFor(nameof(CanCreateRoom))]
    [NotifyPropertyChangedFor(nameof(CanProbeNat))]
    [NotifyPropertyChangedFor(nameof(IsComponentBannerVisible))]
    [NotifyPropertyChangedFor(nameof(ComponentTitle))]
    [NotifyPropertyChangedFor(nameof(InstallActionText))]
    public partial bool IsComponentOutdated { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyPropertyChangedFor(nameof(CanOperate))]
    [NotifyPropertyChangedFor(nameof(CanCreateRoom))]
    [NotifyPropertyChangedFor(nameof(CanProbeNat))]
    [NotifyPropertyChangedFor(nameof(IsComponentBannerVisible))]
    [NotifyPropertyChangedFor(nameof(ComponentTitle))]
    [NotifyPropertyChangedFor(nameof(InstallActionText))]
    public partial bool IsComponentUnverifiable { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyPropertyChangedFor(nameof(CanOperate))]
    [NotifyPropertyChangedFor(nameof(CanCreateRoom))]
    [NotifyPropertyChangedFor(nameof(CanProbeNat))]
    public partial bool IsBackendReady { get; set; }

    public bool IsReady => !IsComponentMissing && !IsComponentOutdated && !IsComponentUnverifiable && IsBackendReady;
    public bool IsComponentBannerVisible => IsComponentMissing || IsComponentOutdated || IsComponentUnverifiable;

    public string ComponentTitle => IsComponentMissing
        ? CommonLanguageManager.Instance.multiplayer_componentMissingTitle.CurrentValue()
        : IsComponentUnverifiable
            ? CommonLanguageManager.Instance.multiplayer_componentUnverifiableTitle.CurrentValue()
            : CommonLanguageManager.Instance.multiplayer_componentOutdatedTitle.CurrentValue();

    public string InstallActionText => IsComponentOutdated || IsComponentUnverifiable
        ? CommonLanguageManager.Instance.multiplayer_updateAndInstall.CurrentValue()
        : CommonLanguageManager.Instance.multiplayer_downloadAndInstall.CurrentValue();
    public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);

    public string LanServerCountText => IsDiscoveringJavaServers
        ? CommonLanguageManager.Instance.multiplayer_detecting.CurrentValue()
        : LanServers.Count > 0
            ? string.Format(CommonLanguageManager.Instance.multiplayer_lanServerCount.CurrentValue(), LanServers.Count)
            : string.Empty;

    public string JavaDiscoveryButtonText => IsDiscoveringJavaServers
        ? CommonLanguageManager.Instance.multiplayer_detecting.CurrentValue()
        : CommonLanguageManager.Instance.multiplayer_detect.CurrentValue();
    public string NatProbeButtonText => IsProbingNat
        ? CommonLanguageManager.Instance.multiplayer_detecting.CurrentValue()
        : CommonLanguageManager.Instance.multiplayer_detectNat.CurrentValue();
    public string CreateRoomButtonText => IsCreatingRoom
        ? CommonLanguageManager.Instance.multiplayer_creating.CurrentValue()
        : CommonLanguageManager.Instance.multiplayer_createRoom.CurrentValue();
    public string JoinRoomButtonText => IsJoiningRoom
        ? CommonLanguageManager.Instance.multiplayer_joining.CurrentValue()
        : CommonLanguageManager.Instance.multiplayer_join.CurrentValue();

    public string JoinCodePlaceholder => IsJava
        ? CommonLanguageManager.Instance.multiplayer_joinCodePlaceholderJava.CurrentValue()
        : CommonLanguageManager.Instance.multiplayer_joinCodePlaceholderBedrock.CurrentValue();

    public bool CanProbeNat => IsReady && !IsBusy && !IsProbingNat;

    [ObservableProperty] public partial string StatusText { get; set; } =
        CommonLanguageManager.Instance.multiplayer_detectingComponent.CurrentValue();
    [ObservableProperty] public partial string PlayerName { get; set; }
    [ObservableProperty] public partial string JoinCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string ManualJavaPort { get; set; } = string.Empty;
    [ObservableProperty] public partial string CurrentRoomCode { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsBedrockPortBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CreateRoomButtonText))]
    [NotifyPropertyChangedFor(nameof(IsRoomOperationInProgress))]
    public partial bool IsCreatingRoom { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JoinRoomButtonText))]
    [NotifyPropertyChangedFor(nameof(IsRoomOperationInProgress))]
    public partial bool IsJoiningRoom { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotInRoom))]
    [NotifyPropertyChangedFor(nameof(ShowJavaRoomActions))]
    [NotifyPropertyChangedFor(nameof(ShowBedrockRoomActions))]
    [NotifyPropertyChangedFor(nameof(CanCreateRoom))]
    public partial bool IsInRoom { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNatSummary))]
    public partial string NatSummary { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateRoom))]
    public partial LanServerEntry? SelectedLanServer { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LanServerCountText))]
    [NotifyPropertyChangedFor(nameof(JavaDiscoveryButtonText))]
    public partial bool IsDiscoveringJavaServers { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProbeNat))]
    [NotifyPropertyChangedFor(nameof(NatProbeButtonText))]
    public partial bool IsProbingNat { get; set; }

    public ObservableCollection<LanServerEntry> LanServers { get; } = [];
    public ObservableCollection<OnlineMember> Members { get; } = [];

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        IsBackendReady = false;
        if (_client is not null) _client.EventReceived -= ClientOnEventReceived;

        _lifetime.Dispose();
    }

    partial void OnStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasStatusText));
    }

    partial void OnPlayerNameChanged(string value)
    {
        Data.ConfigEntry.OnlinePlayerName = value.Trim();
    }

    partial void OnManualJavaPortChanged(string value)
    {
        OnPropertyChanged(nameof(CanCreateRoom));
    }

    public async Task InitializeAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.Info($"[Multiplayer] Initializing {Edition} multiplayer service.");


        if (await Task.Run(GravityConeInstaller.FindInstalled) is not { } installation)
        {
            IsComponentMissing = true;
            StatusText = CommonLanguageManager.Instance.multiplayer_componentNotInstalled.CurrentValue();
            Logger.Warning($"[Multiplayer] {Edition} multiplayer component was not found after {stopwatch.Elapsed}.");
            return;
        }

        switch (await GravityConeInstaller.GetUpdateStatusAsync(_lifetime.Token))
        {
            case ComponentUpdateStatus.Current:
                StatusText = string.Empty;
                try
                {
                    await StartClientAsync(installation);
                    Logger.Info($"[Multiplayer] {Edition} multiplayer service initialized in {stopwatch.Elapsed}.");
                }
                catch (Exception ex)
                {
                    IsBackendReady = false;
                    Logger.Error(ex);
                    Notify(string.Format(CommonLanguageManager.Instance.multiplayer_serviceStartFailed.CurrentValue(),
                        FriendlyError(ex)), NotificationType.Error);
                }

                break;

            case ComponentUpdateStatus.UpdateRequired:
                IsComponentOutdated = true;
                StatusText = CommonLanguageManager.Instance.multiplayer_componentOutdatedTitle.CurrentValue();
                Logger.Warning(
                    $"[Multiplayer] {Edition} multiplayer component requires an update after {stopwatch.Elapsed}.");
                await StopSharedClientIfOutdatedAsync();
                break;

            case ComponentUpdateStatus.Unknown:
                IsComponentUnverifiable = true;
                StatusText = CommonLanguageManager.Instance.multiplayer_componentVerifyFailed.CurrentValue();
                Logger.Warning(
                    $"[Multiplayer] {Edition} multiplayer component version could not be verified after {stopwatch.Elapsed}.");
                break;
        }
    }

    private async Task StopSharedClientIfOutdatedAsync()
    {
        if (SharedInstallation is null) return;
        await SharedClientStartLock.WaitAsync(_lifetime.Token);
        try
        {
            if (SharedClient.IsRunning)
            {
                Logger.Warning(
                    $"[Multiplayer] Detected outdated component; stopping shared client {SharedInstallation.CliPath}.");
                await SharedClient.StopAsync();
            }
        }
        finally
        {
            SharedClientStartLock.Release();
        }

        SharedInstallation = null;
    }

    public void Activate()
    {
        _isActive = true;
    }

    public void Deactivate()
    {
        _isActive = false;
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (IsBusy) return;
        Logger.Info($"[Multiplayer] Starting component installation for {Edition} edition.");
        IsBusy = true;
        StatusText = CommonLanguageManager.Instance.multiplayer_downloadingComponent.CurrentValue();
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = CommonLanguageManager.Instance.multiplayer_downloadComponent.CurrentValue(),
            Description = CommonLanguageManager.Instance.multiplayer_preparingDownload.CurrentValue(),
            Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = CommonLanguageManager.Instance.multiplayer_cancelDownload.CurrentValue(),
                    Description = CommonLanguageManager.Instance.multiplayer_cancelComponentDownload.CurrentValue(),
                    IconKey = "Cancel",
                    ExecuteAsync = (managedTask, _) =>
                    {
                        managedTask.RequestCancellation();
                        return Task.CompletedTask;
                    },
                    CanExecute = managedTask => managedTask.CanBeCancelled,
                    IsVisible = managedTask => !managedTask.IsTerminal
                }
            ]
        }, async context =>
        {
            var progress = new Progress<(double? Progress, string Message)>(item => Dispatcher.UIThread.Post(() =>
            {
                if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                context.ReportProgress(item.Progress);
                context.SetDescription(item.Message);
            }));
            var installation = await GravityConeInstaller.EnsureInstalledAsync(progress, context.CancellationToken,
                IsComponentOutdated || IsComponentUnverifiable);
            context.ReportProgress(1);
            context.SetDescription(CommonLanguageManager.Instance.multiplayer_componentDownloaded.CurrentValue());
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsComponentMissing = false;
                IsComponentOutdated = false;
                IsComponentUnverifiable = false;
            });
            await StartClientAsync(installation);
        });
        task.Start();
        _ = ObserveInstallationAsync(task);
    }

    private async Task ObserveInstallationAsync(ManagedTask task)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await task.Completion;
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[Multiplayer] Component installation was cancelled after {stopwatch.Elapsed}: {exception}");
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsBusy = false;
                StatusText = IsComponentMissing
                    ? CommonLanguageManager.Instance.multiplayer_componentNotInstalled.CurrentValue()
                    : IsComponentOutdated
                        ? CommonLanguageManager.Instance.multiplayer_componentOutdatedTitle.CurrentValue()
                        : IsComponentUnverifiable
                            ? CommonLanguageManager.Instance.multiplayer_componentVerifyFailed.CurrentValue()
                            : string.Empty;
                if (task.Status == ManagedTaskStatus.Completed)
                {
                    Logger.Info($"[Multiplayer] Component installation completed in {stopwatch.Elapsed}.");
                    Notify(CommonLanguageManager.Instance.multiplayer_componentInstalled.CurrentValue(),
                        NotificationType.Success);
                }
                else if (task.Status == ManagedTaskStatus.Faulted)
                {
                    Logger.Warning(
                        $"[Multiplayer] Component installation failed after {stopwatch.Elapsed}: {task.Exception}");
                    Notify(CommonLanguageManager.Instance.multiplayer_componentDownloadFailed.CurrentValue(),
                        NotificationType.Error);
                }
            });
        }
    }

    private async Task StartClientAsync(GravityConeInstallation installation)
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.Info($"[Multiplayer] Starting {Edition} multiplayer client from {installation.CliPath}.");
        await SharedClientStartLock.WaitAsync(_lifetime.Token);
        try
        {
            if (SharedInstallation is { } current &&
                !string.Equals(current.CliPath, installation.CliPath, StringComparison.Ordinal))
            {
                Logger.Info($"[Multiplayer] Component version changed ({current.CliPath} -> {installation.CliPath}); " +
                            $"restarting shared client.");
                await SharedClient.RestartAsync(installation, _lifetime.Token);
                SharedInstallation = installation;
            }
            else
            {
                SharedInstallation ??= installation;
                await SharedClient.StartAsync(SharedInstallation, CancellationToken.None);
            }
        }
        finally
        {
            SharedClientStartLock.Release();
        }

        _installation = SharedInstallation;
        _client = SharedClient;
        _client.EventReceived -= ClientOnEventReceived;
        _client.EventReceived += ClientOnEventReceived;
        await _client.StartAsync(installation, _lifetime.Token);
        IsBackendReady = true;
        StatusText = string.Empty;
        await RefreshRoomStatusAsync(true, Edition);
        ApplyActiveRoomState();
        if (IsJava) await DiscoverJavaServersAsync();
        Logger.Info($"[Multiplayer] {Edition} multiplayer client started in {stopwatch.Elapsed}.");
    }

    [RelayCommand]
    private async Task DiscoverJavaServersAsync()
    {
        if (_client is null || IsBusy || IsDiscoveringJavaServers) return;
        IsDiscoveringJavaServers = true;
        var stopwatch = Stopwatch.StartNew();
        Logger.Info("[Multiplayer] Starting Java LAN server discovery.");
        try
        {
            await _client.RequestAsync("lan.start_discovery", cancellationToken: _lifetime.Token);
            await Task.Delay(TimeSpan.FromSeconds(3), _lifetime.Token);
            var response = await _client.RequestAsync("lan.list_servers", cancellationToken: _lifetime.Token);
            UpdateLanServers(response.Data);
            Logger.Info($"[Multiplayer] Java LAN discovery found {LanServers.Count} server(s) in {stopwatch.Elapsed}.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            Notify(string.Format(CommonLanguageManager.Instance.multiplayer_lanDiscoveryFailed.CurrentValue(),
                FriendlyError(ex)), NotificationType.Error);
        }
        finally
        {
            IsDiscoveringJavaServers = false;
        }
    }

    [RelayCommand]
    private async Task CreateRoomAsync()
    {
        if (_client is null || !CanCreateRoom || !ValidatePlayerName()) return;
        var edition = Edition;
        var stopwatch = Stopwatch.StartNew();
        Logger.Info($"[Multiplayer] Creating {edition} room for player {PlayerName.Trim()}.");
        IsBusy = true;
        IsCreatingRoom = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _roomOperationCancellation = cancellation;
        try
        {
            await RefreshRoomStatusAsync();
            if (IsInRoom)
            {
                Notify(CommonLanguageManager.Instance.multiplayer_roomAlreadyRunning.CurrentValue(),
                    NotificationType.Warning);
                return;
            }

            object parameters = edition == MinecraftEdition.Java
                ? new { mc_port = ResolveJavaPort()!.Value, player_name = PlayerName.Trim() }
                : new { player_name = PlayerName.Trim(), protocol = "paperconnect" };
            var response = await _client.RequestAsync("room.create", parameters,
                timeout: TimeSpan.FromSeconds(35), cancellationToken: cancellation.Token);
            ApplyRoomData(response.Data, "host", edition);
            Logger.Info($"[Multiplayer] Created {edition} room {CurrentRoomCode} in {stopwatch.Elapsed}.");
            Notify(CommonLanguageManager.Instance.multiplayer_roomCreated.CurrentValue(), NotificationType.Success);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Logger.Debug($"[Multiplayer] {edition} room creation cancelled after {stopwatch.Elapsed}.");
            await RollbackCancelledRoomOperationAsync(edition);
            Notify(CommonLanguageManager.Instance.multiplayer_createCancelled.CurrentValue(),
                NotificationType.Information);
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            Notify(string.Format(CommonLanguageManager.Instance.multiplayer_createFailed.CurrentValue(),
                FriendlyError(ex)), NotificationType.Error);
        }
        finally
        {
            if (ReferenceEquals(_roomOperationCancellation, cancellation)) _roomOperationCancellation = null;
            IsCreatingRoom = false;
            IsBusy = false;
        }
    }

    public async Task CreateJavaRoomFromPortAsync(string portText)
    {
        if (_client is null || IsInRoom || IsBusy) return;
        if (!int.TryParse(portText.Trim(), out var port) || port is < 1025 or > 65535)
        {
            Notify(CommonLanguageManager.Instance.multiplayer_portRangeInvalid.CurrentValue(), NotificationType.Warning);
            return;
        }

        IsBusy = true;
        try
        {
            if (!await IsLocalPortOpenAsync(port, _lifetime.Token))
            {
                Notify(string.Format(CommonLanguageManager.Instance.multiplayer_portNoService.CurrentValue(), port),
                    NotificationType.Warning);
                return;
            }

            ManualJavaPort = port.ToString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Notify(string.Format(CommonLanguageManager.Instance.multiplayer_portCheckFailed.CurrentValue(),
                FriendlyError(ex)), NotificationType.Error);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await CreateRoomAsync();
    }

    [RelayCommand]
    private async Task JoinRoomAsync()
    {
        if (_client is null || IsBusy || !ValidatePlayerName()) return;
        var edition = Edition;
        var code = JoinCode.Trim().ToUpperInvariant();
        var stopwatch = Stopwatch.StartNew();
        var prefix = edition == MinecraftEdition.Java ? "U/" : "P/";
        if (!code.StartsWith(prefix, StringComparison.Ordinal))
        {
            Notify(string.Format(CommonLanguageManager.Instance.multiplayer_roomCodePrefix.CurrentValue(),
                EditionTitle, prefix), NotificationType.Warning);
            return;
        }

        IsBusy = true;
        IsJoiningRoom = true;
        Logger.Info($"[Multiplayer] Joining {edition} room {code} as {PlayerName.Trim()}.");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _roomOperationCancellation = cancellation;
        try
        {
            var progress = new Progress<GravityConeProgress>(_ => { });
            object parameters = edition == MinecraftEdition.Java
                ? new { code, player_name = PlayerName.Trim() }
                : new { code, player_name = PlayerName.Trim(), protocol = "paperconnect" };
            var response = await _client.RequestAsync("room.join", parameters,
                progress, TimeSpan.FromSeconds(60), cancellation.Token);
            ApplyRoomData(response.Data, "guest", edition);
            Logger.Info($"[Multiplayer] Joined {edition} room {CurrentRoomCode} in {stopwatch.Elapsed}.");
            Notify(edition == MinecraftEdition.Bedrock
                ? CommonLanguageManager.Instance.multiplayer_bedrockChannelConnected.CurrentValue()
                : CommonLanguageManager.Instance.multiplayer_roomJoined.CurrentValue(), NotificationType.Success);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Logger.Debug($"[Multiplayer] Joining {edition} room {code} was cancelled after {stopwatch.Elapsed}.");
            await RollbackCancelledRoomOperationAsync(edition);
            Notify(CommonLanguageManager.Instance.multiplayer_joinCancelled.CurrentValue(),
                NotificationType.Information);
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            Notify(string.Format(CommonLanguageManager.Instance.multiplayer_joinFailed.CurrentValue(),
                FriendlyError(ex)), NotificationType.Error);
        }
        finally
        {
            if (ReferenceEquals(_roomOperationCancellation, cancellation)) _roomOperationCancellation = null;
            IsJoiningRoom = false;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelRoomOperation()
    {
        if (_roomOperationCancellation is not { IsCancellationRequested: false } cancellation) return;
        cancellation.Cancel();
        Notify(CommonLanguageManager.Instance.multiplayer_cancellingOperation.CurrentValue(),
            NotificationType.Information);
    }

    private async Task RollbackCancelledRoomOperationAsync(MinecraftEdition edition)
    {
        if (_client is null || _lifetime.IsCancellationRequested) return;

        try
        {
            var status = await _client.RequestAsync("room.status", RoomParameters(edition),
                timeout: TimeSpan.FromSeconds(4), cancellationToken: _lifetime.Token);
            var role = status.Data.TryGetProperty("role", out var roleValue) ? roleValue.GetString() : "none";
            if (role == "none") return;

            await _client.RequestAsync(role == "host" ? "room.stop" : "room.leave", RoomParameters(edition),
                timeout: TimeSpan.FromSeconds(8), cancellationToken: _lifetime.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !_lifetime.IsCancellationRequested)
        {
            Logger.Warning($"[Multiplayer] Failed to roll back cancelled room operation: {ex}");
        }
        finally
        {
            ClearRoom(edition);
        }
    }

    [RelayCommand]
    private async Task ConfirmMinecraftEndedAsync()
    {
        if (_client is null || !IsBedrockPortBusy) return;
        IsBusy = true;
        try
        {
            await _client.RequestAsync("room.confirm_minecraft_ended", timeout: TimeSpan.FromSeconds(8),
                cancellationToken: _lifetime.Token);
            IsBedrockPortBusy = false;
            Notify(CommonLanguageManager.Instance.multiplayer_reconnectingBedrock.CurrentValue(),
                NotificationType.Information);
        }
        catch (Exception ex)
        {
            Notify(string.Format(CommonLanguageManager.Instance.multiplayer_continueConnectFailed.CurrentValue(),
                FriendlyError(ex)), NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LeaveRoomAsync()
    {
        if (_client is null || !IsInRoom) return;
        IsBusy = true;
        var stopwatch = Stopwatch.StartNew();
        Logger.Info($"[Multiplayer] Leaving {Edition} room {CurrentRoomCode}.");
        try
        {
            var room = GetRoomState(Edition);
            await _client.RequestAsync(room.Role == "host" ? "room.stop" : "room.leave", RoomParameters(Edition),
                timeout: TimeSpan.FromSeconds(8), cancellationToken: _lifetime.Token);
            var otherEdition = Edition == MinecraftEdition.Java ? MinecraftEdition.Bedrock : MinecraftEdition.Java;
            if (await CanRestartSharedClientAsync(otherEdition)) await RestartClientAsync();
            ClearRoom(Edition);
            Logger.Info($"[Multiplayer] Left {Edition} room in {stopwatch.Elapsed}.");
            Notify(CommonLanguageManager.Instance.multiplayer_roomLeft.CurrentValue(), NotificationType.Success);
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            Notify(string.Format(CommonLanguageManager.Instance.multiplayer_leaveFailed.CurrentValue(),
                FriendlyError(ex)), NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> CanRestartSharedClientAsync(MinecraftEdition otherEdition)
    {
        if (_client is null) return false;

        try
        {
            var status = await _client.RequestAsync("room.status", RoomParameters(otherEdition),
                timeout: TimeSpan.FromSeconds(4), cancellationToken: _lifetime.Token);
            return !status.Data.TryGetProperty("role", out var role) || role.GetString() == "none";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !_lifetime.IsCancellationRequested)
        {
            Logger.Warning($"[Multiplayer] Skipped CLI restart because the other room status is unavailable: {ex}");
            return false;
        }
    }

    private async Task RestartClientAsync()
    {
        if (_installation is null)
            throw new InvalidOperationException(CommonLanguageManager.Instance.multiplayer_installInfoUnavailable.CurrentValue());

        await SharedClientStartLock.WaitAsync(_lifetime.Token);
        try
        {
            await SharedClient.RestartAsync(_installation, _lifetime.Token);
        }
        finally
        {
            SharedClientStartLock.Release();
        }
    }

    [RelayCommand]
    private async Task ProbeNatAsync()
    {
        if (_client is null || !CanProbeNat) return;
        IsProbingNat = true;
        var stopwatch = Stopwatch.StartNew();
        Logger.Info($"[Multiplayer] Starting NAT probe for {Edition} edition.");
        try
        {
            var response = await _client.RequestAsync("stun.probe", timeout: TimeSpan.FromSeconds(15),
                cancellationToken: _lifetime.Token);
            var udp = response.Data.TryGetProperty("udp_nat_type", out var udpValue) ? udpValue.GetInt32() : 0;
            var tcp = response.Data.TryGetProperty("tcp_nat_type", out var tcpValue) ? tcpValue.GetInt32() : 0;
            var ip = response.Data.TryGetProperty("public_ip", out var ipValue) &&
                     ipValue.ValueKind == JsonValueKind.Array
                ? string.Join(", ", ipValue.EnumerateArray().Select(item => item.GetString()))
                : CommonLanguageManager.Instance.account_unknown.CurrentValue();
            NatSummary = string.Format(CommonLanguageManager.Instance.multiplayer_natSummary.CurrentValue(), ip,
                NatName(udp), NatName(tcp));
            Logger.Info($"[Multiplayer] NAT probe completed for {Edition} in {stopwatch.Elapsed}: {NatSummary}.");
            Notify(CommonLanguageManager.Instance.multiplayer_natComplete.CurrentValue(), NotificationType.Success);
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            Notify(string.Format(CommonLanguageManager.Instance.multiplayer_natFailed.CurrentValue(),
                FriendlyError(ex)), NotificationType.Error);
        }
        finally
        {
            IsProbingNat = false;
        }
    }

    private void ClientOnEventReceived(object? sender, GravityConeEvent e)
    {
        if (!_isActive) return;
        if (IsJava && e.Name.StartsWith("paperconnect.", StringComparison.Ordinal)) return;
        if (IsBedrock && (e.Name.StartsWith("room.", StringComparison.Ordinal) ||
                          e.Name.StartsWith("lan.", StringComparison.Ordinal))) return;

        Dispatcher.UIThread.Post(() =>
        {
            switch (e.Name)
            {
                case "lan.server_found": AddLanServer(e.Data); break;
                case "lan.server_lost": RemoveLanServer(e.Data); break;
                case "room.player_joined":
                case "room.player_left":
                case "room.guest_player_list_updated":
                    _ = RefreshRoomStatusAsync(edition: MinecraftEdition.Java);
                    break;
                case "paperconnect.room.player_joined":
                case "paperconnect.room.player_left":
                    _ = RefreshRoomStatusAsync(edition: MinecraftEdition.Bedrock);
                    break;
                case "paperconnect.room.info":
                    ApplyRoomData(e.Data, _bedrockRoom.Role, MinecraftEdition.Bedrock); break;
                case "room.closed":
                case "room.disconnected":
                    ClearRoom(MinecraftEdition.Java);
                    Notify(CommonLanguageManager.Instance.multiplayer_roomConnectionClosed.CurrentValue(),
                        NotificationType.Information);
                    break;
                case "paperconnect.room.closed":
                case "paperconnect.room.disconnected":
                case "paperconnect.connection.closed":
                case "paperconnect.connection.disconnected":
                    ClearRoom(MinecraftEdition.Bedrock);
                    Notify(CommonLanguageManager.Instance.multiplayer_roomConnectionClosed.CurrentValue(),
                        NotificationType.Information);
                    break;
                case "paperconnect.connection.ready":
                    IsBedrockPortBusy = false;
                    Notify(CommonLanguageManager.Instance.multiplayer_connectionReady.CurrentValue(),
                        NotificationType.Success);
                    break;
                case "paperconnect.connection.port_busy":
                    IsBedrockPortBusy = true;
                    Notify(CommonLanguageManager.Instance.multiplayer_portBusy.CurrentValue(),
                        NotificationType.Warning);
                    break;
                case "paperconnect.connection.error":
                    ClearRoom(MinecraftEdition.Bedrock);
                    Notify(CommonLanguageManager.Instance.multiplayer_bedrockConnectionFailed.CurrentValue(),
                        NotificationType.Error);
                    break;
            }
        });
    }

    private async Task RefreshRoomStatusAsync(bool force = false, MinecraftEdition? edition = null)
    {
        if (_client is null) return;
        if (Interlocked.CompareExchange(ref _isRefreshingRoomStatus, 1, 0) != 0) return;

        try
        {
            if (!force && DateTimeOffset.UtcNow - _lastRoomStatusRequest < TimeSpan.FromSeconds(1)) return;
            _lastRoomStatusRequest = DateTimeOffset.UtcNow;
            var targetEdition = edition ?? Edition;
            var response = await _client.RequestAsync("room.status", RoomParameters(targetEdition),
                timeout: TimeSpan.FromSeconds(4),
                cancellationToken: _lifetime.Token);
            var role = response.Data.TryGetProperty("role", out var roleValue)
                ? roleValue.GetString() ?? "none"
                : GetRoomState(targetEdition).Role;
            if (role == "none")
            {
                ClearRoom(targetEdition);
                return;
            }

            ApplyRoomData(response.Data, role, targetEdition);
        }
        catch (OperationCanceledException exception) when (_lifetime.IsCancellationRequested)
        {
            Logger.Debug($"[Multiplayer] Room status refresh cancelled: {exception}");
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Multiplayer] Room status refresh failed: {exception}");
        }
        finally
        {
            Volatile.Write(ref _isRefreshingRoomStatus, 0);
        }
    }

    private void ApplyRoomData(JsonElement data, string role, MinecraftEdition edition)
    {
        var room = GetRoomState(edition);
        room.Role = role;
        room.Code = GetString(data, "code") ?? GetString(data, "room_code") ?? room.Code;
        room.Members.Clear();
        if (data.TryGetProperty("players", out var players) && players.ValueKind == JsonValueKind.Array)
            foreach (var player in players.EnumerateArray())
            {
                var name = GetString(player, "name") ?? GetString(player, "player") ??
                           CommonLanguageManager.Instance.multiplayer_unknownMember.CurrentValue();
                var vendor = GetString(player, "vendor") ?? GetString(player, "clientId") ?? string.Empty;
                var kind = GetString(player, "kind") ??
                           (player.TryGetProperty("isRoomHost", out var host) && host.ValueKind == JsonValueKind.True
                               ? "HOST"
                               : "GUEST");
                room.Members.Add(new OnlineMember { Name = name, Vendor = vendor, Kind = kind });
            }

        if (edition == Edition) ApplyActiveRoomState();
    }

    private void UpdateLanServers(JsonElement data)
    {
        LanServers.Clear();
        if (data.TryGetProperty("servers", out var servers) && servers.ValueKind == JsonValueKind.Array)
        {
            foreach (var server in servers.EnumerateArray()) AddLanServer(server);
            SelectedLanServer = LanServers.FirstOrDefault();
        }

        OnPropertyChanged(nameof(LanServerCountText));
    }

    private void AddLanServer(JsonElement data)
    {
        var ip = GetString(data, "ip") ?? "127.0.0.1";
        if (!data.TryGetProperty("port", out var portValue) || !portValue.TryGetInt32(out var port)) return;
        if (LanServers.Any(item => item.Ip == ip && item.Port == port)) return;
        LanServers.Add(new LanServerEntry
        {
            Motd = GetString(data, "motd") ?? CommonLanguageManager.Instance.multiplayer_minecraftWorld.CurrentValue(),
            Ip = ip, Port = port
        });
        SelectedLanServer ??= LanServers[^1];
        OnPropertyChanged(nameof(LanServerCountText));
    }

    private void RemoveLanServer(JsonElement data)
    {
        var ip = GetString(data, "ip");
        var port = data.TryGetProperty("port", out var value) && value.TryGetInt32(out var result) ? result : 0;
        var item = LanServers.FirstOrDefault(server => server.Ip == ip && server.Port == port);
        if (item is not null) LanServers.Remove(item);
        OnPropertyChanged(nameof(LanServerCountText));
    }

    private int? ResolveJavaPort()
    {
        if (int.TryParse(ManualJavaPort.Trim(), out var manual) && manual is >= 1025 and <= 65535) return manual;
        return SelectedLanServer?.Port is >= 1025 and <= 65535 ? SelectedLanServer.Port : null;
    }

    private static async Task<bool> IsLocalPortOpenAsync(int port, CancellationToken cancellationToken)
    {
        foreach (var address in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
        {
            using var client = new TcpClient(address.AddressFamily);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await client.ConnectAsync(address, port, timeout.Token);
                return true;
            }
            catch (SocketException)
            {
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }

        return false;
    }

    private bool ValidatePlayerName()
    {
        if (!string.IsNullOrWhiteSpace(PlayerName)) return true;
        Notify(CommonLanguageManager.Instance.multiplayer_enterPlayerName.CurrentValue(), NotificationType.Warning);
        return false;
    }

    private void ClearRoom(MinecraftEdition edition)
    {
        var room = GetRoomState(edition);
        room.Role = "none";
        room.Code = string.Empty;
        room.Members.Clear();
        if (edition == MinecraftEdition.Bedrock) IsBedrockPortBusy = false;
        if (edition == Edition) ApplyActiveRoomState();
    }

    private RoomState GetRoomState(MinecraftEdition edition)
    {
        return edition == MinecraftEdition.Java ? _javaRoom : _bedrockRoom;
    }

    private static object RoomParameters(MinecraftEdition edition)
    {
        return edition == MinecraftEdition.Bedrock
            ? new { protocol = "paperconnect" }
            : new { };
    }

    private void ApplyActiveRoomState()
    {
        var room = GetRoomState(Edition);
        CurrentRoomCode = room.Code;
        IsInRoom = !string.IsNullOrWhiteSpace(room.Code);
        Members.Clear();
        foreach (var member in room.Members) Members.Add(member);
        OnPropertyChanged(nameof(MemberCountText));
    }

    private static string? GetString(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string FriendlyError(Exception exception)
    {
        return exception is GravityConeException { Code: "INTERNAL_ERROR" } &&
               exception.Message.Contains(LinguaSentinels.NotDetectedMarker, StringComparison.Ordinal)
            ? CommonLanguageManager.Instance.multiplayer_noOpenWorld.CurrentValue()
            : exception.Message;
    }

    private static string NatName(int type)
    {
        return type switch
        {
            1 => CommonLanguageManager.Instance.multiplayer_natOpenNetwork.CurrentValue(),
            2 => CommonLanguageManager.Instance.multiplayer_natSymmetricFirewall.CurrentValue(),
            3 => CommonLanguageManager.Instance.multiplayer_natFullCone.CurrentValue(),
            4 => CommonLanguageManager.Instance.multiplayer_natRestrictedCone.CurrentValue(),
            5 => CommonLanguageManager.Instance.multiplayer_natPortRestricted.CurrentValue(),
            6 => CommonLanguageManager.Instance.multiplayer_natSymmetricIncrement.CurrentValue(),
            7 => CommonLanguageManager.Instance.multiplayer_natSymmetric.CurrentValue(),
            _ => CommonLanguageManager.Instance.account_unknown.CurrentValue()
        };
    }

    [RelayCommand]
    private void ClearJoinCode()
    {
        JoinCode = string.Empty;
    }

    private static void Notify(string message, NotificationType type)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: { } window
            })
            window.Notice(message, type);
    }

    private sealed class RoomState
    {
        public string Role { get; set; } = "none";
        public string Code { get; set; } = string.Empty;
        public List<OnlineMember> Members { get; } = [];
    }
}
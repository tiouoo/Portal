using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Module.AggregatedSearch;
using Portal.Module.DefaultPage;
using Portal.Module.Multiplayer;
using Portal.Const;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Gateway;
using Tio.Avalonia.Standard.Tab.Interface;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Modules.DiskIO;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages;

[DefaultPage("联机")]
[AggregatedSearchPage("联机", "联机", "Multiplayer")]
public partial class MultiplayerPage : UserControl, ITioTabPage
{
    public MultiplayerPage() : this(MinecraftEdition.Java)
    {
    }

    public MultiplayerPage(MinecraftEdition edition = MinecraftEdition.Java)
    {
        InitializeComponent();
        PageInfo.Title = OperatingSystem.IsWindows()
            ? edition == MinecraftEdition.Java ? "联机 (Java)" : "联机 (基岩)"
            : "联机";
        ViewModel = new MultiplayerPageViewModel(edition);
        DataContext = ViewModel;
        Loaded += OnLoaded;
    }

    public MultiplayerPageViewModel ViewModel { get; }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = "联机",
        Icon = StreamGeometry.Parse(
            "F1 M640,640z M0,0z M451.5,160C434.9,160 418.8,164.5 404.7,172.7 388.9,156.7 370.5,143.3 350.2,133.2 378.4,109.2 414.3,96 451.5,96 537.9,96 608,166 608,252.5 608,294 591.5,333.8 562.2,363.1L491.1,434.2C461.8,463.5 422,480 380.5,480 294.1,480 224,410 224,323.5 224,305.8 238.3,291.5 256,291.5 273.7,291.5 288,305.8 288,323.5 288,374.6 329.4,416 380.5,416 405,416 428.5,406.3 445.9,388.9L517,317.8C534.3,300.5 544,277 544,252.5 544,201.4 502.6,160 451.5,160z M259.5,224C235,224 211.5,233.7 194.1,251.1L123,322.2C105.7,339.5 96,363 96,387.5 96,438.6 137.4,480 188.5,480 205.1,480 221.2,475.5 235.3,467.3 251.1,483.3 269.5,496.7 289.8,506.8 261.6,530.8 225.7,544 188.5,544 102.1,544 32,474 32,387.5 32,346 48.5,306.2 77.8,276.9L148.9,205.8C178.2,176.5 218,160 259.5,160 345.9,160 416,230 416,316.5 416,334.2 401.7,348.5 384,348.5 366.3,348.5 352,334.2 352,316.5 352,265.4 310.6,224 259.5,224z")
    };

    public TabEntry HostTab { get; set; } = null!;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
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
        this.FindControl<ContentControl>("Frame")!.Content = new Components.MultiplayerContentPage(ViewModel);
    }

    public void OnClose()
    {
        Loaded -= OnLoaded;
        ViewModel.Deactivate();
        DataContext = null;
    }
}

public partial class MultiplayerPageViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly GravityConeClient SharedClient = new();
    private static readonly SemaphoreSlim SharedClientStartLock = new(1, 1);
    private static GravityConeInstallation? SharedInstallation;
    private readonly CancellationTokenSource _lifetime = new();
    private GravityConeClient? _client;
    private GravityConeInstallation? _installation;
    private bool _disposed;
    private bool _isActive;
    private MinecraftEdition _edition = MinecraftEdition.Java;
    private readonly RoomState _javaRoom = new();
    private readonly RoomState _bedrockRoom = new();
    private DateTimeOffset _lastRoomStatusRequest = DateTimeOffset.MinValue;
    private int _isRefreshingRoomStatus;
    private CancellationTokenSource? _roomOperationCancellation;

    public MultiplayerPageViewModel(MinecraftEdition edition)
    {
        _edition = edition;
        PlayerName = string.IsNullOrWhiteSpace(Data.ConfigEntry.OnlinePlayerName)
            ? Data.ConfigEntry.UsingMinecraftMinecraftAccount?.Name ?? string.Empty
            : Data.ConfigEntry.OnlinePlayerName;
    }

    public MinecraftEdition Edition => _edition;
    public bool IsJava => _edition == MinecraftEdition.Java;
    public bool IsBedrock => _edition == MinecraftEdition.Bedrock;
    public string EditionTitle => "联机";
    public bool IsNotBusy => !IsBusy;
    public bool CanOperate => IsReady && !IsBusy;
    public bool IsNotInRoom => !IsInRoom;
    public bool ShowJavaRoomActions => IsJava && IsNotInRoom;
    public bool ShowBedrockRoomActions => IsBedrock && IsNotInRoom;
    public bool CanCreateRoom => CanOperate && IsNotInRoom && (IsBedrock || ResolveJavaPort() is not null);
    public bool HasNatSummary => !string.IsNullOrWhiteSpace(NatSummary);
    public string MemberCountText => $"{Members.Count} 人";
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
    public partial bool IsComponentMissing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyPropertyChangedFor(nameof(CanOperate))]
    [NotifyPropertyChangedFor(nameof(CanCreateRoom))]
    [NotifyPropertyChangedFor(nameof(CanProbeNat))]
    public partial bool IsBackendReady { get; set; }

    public bool IsReady => !IsComponentMissing && IsBackendReady;
    public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);

    public string LanServerCountText => IsDiscoveringJavaServers ? "正在检测中" :
        LanServers.Count > 0 ? $"检测到{LanServers.Count}个开放的世界" : string.Empty;

    public string JavaDiscoveryButtonText => IsDiscoveringJavaServers ? "检测中" : "检测";
    public string NatProbeButtonText => IsProbingNat ? "检测中" : "检测 NAT";
    public string CreateRoomButtonText => IsCreatingRoom ? "创建中" : "创建房间";
    public string JoinRoomButtonText => IsJoiningRoom ? "加入中" : "加入";
    public string JoinCodePlaceholder => IsJava
        ? "请输入房间码（U/XXXX-XXXX-XXXX-XXXX）"
        : "请输入房间码（P/XXXX-XXXX-XXXX-XXXX）";
    public bool CanProbeNat => IsReady && !IsBusy && !IsProbingNat;

    [ObservableProperty] public partial string StatusText { get; set; } = "正在检测联机组件";
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

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatusText));

    public ObservableCollection<LanServerEntry> LanServers { get; } = [];
    public ObservableCollection<OnlineMember> Members { get; } = [];

    partial void OnPlayerNameChanged(string value)
    {
        Data.ConfigEntry.OnlinePlayerName = value.Trim();
    }

    partial void OnManualJavaPortChanged(string value) => OnPropertyChanged(nameof(CanCreateRoom));

    public async Task InitializeAsync()
    {
        // FindInstalled() does synchronous file IO and JSON deserialization;
        // run it on a background thread to avoid blocking the UI on first open.
        var installation = await Task.Run(GravityConeInstaller.FindInstalled);
        IsComponentMissing = installation is null;
        StatusText = installation is null ? "联机组件未安装" : string.Empty;
        if (installation is null) return;
        try
        {
            await StartClientAsync(installation);
        }
        catch (Exception ex)
        {
            IsBackendReady = false;
            Notify($"联机服务启动失败：{FriendlyError(ex)}", NotificationType.Error);
        }
    }

    public void Activate()
    {
        _isActive = true;
    }

    public void Deactivate() => _isActive = false;

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "正在下载联机组件";
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = "下载联机组件",
            Description = "正在准备下载",
            Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = "取消下载",
                    Description = "取消联机组件下载。",
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
            var installation = await GravityConeInstaller.EnsureInstalledAsync(progress, context.CancellationToken);
            context.ReportProgress(1);
            context.SetDescription("联机组件下载完成");
            await Dispatcher.UIThread.InvokeAsync(() => IsComponentMissing = false);
            await StartClientAsync(installation);
        });
        task.Start();
        _ = ObserveInstallationAsync(task);
    }

    private async Task ObserveInstallationAsync(ManagedTask task)
    {
        try
        {
            await task.Completion;
        }
        catch
        {
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsBusy = false;
                StatusText = IsComponentMissing ? "联机组件未安装" : string.Empty;
                if (task.Status == ManagedTaskStatus.Completed)
                    Notify("联机组件已安装", NotificationType.Success);
                else if (task.Status == ManagedTaskStatus.Faulted)
                    Notify("联机组件下载失败", NotificationType.Error);
            });
        }
    }

    private async Task StartClientAsync(GravityConeInstallation installation)
    {
        await SharedClientStartLock.WaitAsync(_lifetime.Token);
        try
        {
            SharedInstallation ??= installation;
            await SharedClient.StartAsync(SharedInstallation, CancellationToken.None);
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
    }

    [RelayCommand]
    private async Task DiscoverJavaServersAsync()
    {
        if (_client is null || IsBusy || IsDiscoveringJavaServers) return;
        IsDiscoveringJavaServers = true;
        try
        {
            await _client.RequestAsync("lan.start_discovery", cancellationToken: _lifetime.Token);
            await Task.Delay(TimeSpan.FromSeconds(3), _lifetime.Token);
            var response = await _client.RequestAsync("lan.list_servers", cancellationToken: _lifetime.Token);
            UpdateLanServers(response.Data);
        }
        catch (Exception ex)
        {
            Notify($"局域网世界检测失败：{FriendlyError(ex)}", NotificationType.Error);
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
        IsBusy = true;
        IsCreatingRoom = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _roomOperationCancellation = cancellation;
        try
        {
            await RefreshRoomStatusAsync();
            if (IsInRoom)
            {
                Notify("已有房间正在运行，请先关闭并离开", NotificationType.Warning);
                return;
            }

            object parameters = edition == MinecraftEdition.Java
                ? new { mc_port = ResolveJavaPort()!.Value, player_name = PlayerName.Trim() }
                : new { player_name = PlayerName.Trim(), protocol = "paperconnect" };
            var response = await _client.RequestAsync("room.create", parameters,
                timeout: TimeSpan.FromSeconds(35), cancellationToken: cancellation.Token);
            ApplyRoomData(response.Data, "host", edition);
            Notify("房间已创建", NotificationType.Success);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await RollbackCancelledRoomOperationAsync(edition);
            Notify("已取消创建房间", NotificationType.Information);
        }
        catch (Exception ex)
        {
            Notify($"创建失败：{FriendlyError(ex)}", NotificationType.Error);
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
            Notify("请输入 1025 到 65535 之间的局域网端口", NotificationType.Warning);
            return;
        }

        IsBusy = true;
        try
        {
            if (!await IsLocalPortOpenAsync(port, _lifetime.Token))
            {
                Notify($"端口 {port} 未检测到 Minecraft 服务，请先在游戏中开放局域网世界", NotificationType.Warning);
                return;
            }

            ManualJavaPort = port.ToString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Notify($"端口检测失败：{FriendlyError(ex)}", NotificationType.Error);
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
        var prefix = edition == MinecraftEdition.Java ? "U/" : "P/";
        if (!code.StartsWith(prefix, StringComparison.Ordinal))
        {
            Notify($"{EditionTitle}房间码必须以 {prefix} 开头", NotificationType.Warning);
            return;
        }

        IsBusy = true;
        IsJoiningRoom = true;
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
            Notify(edition == MinecraftEdition.Bedrock ? "控制通道已连接，正在建立游戏连接" : "已加入房间", NotificationType.Success);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await RollbackCancelledRoomOperationAsync(edition);
            Notify("已取消加入房间", NotificationType.Information);
        }
        catch (Exception ex)
        {
            Notify($"加入失败：{FriendlyError(ex)}", NotificationType.Error);
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
        Notify("正在取消联机操作", NotificationType.Information);
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
            Logger.Warning($"[Multiplayer] Failed to roll back cancelled room operation: {ex.Message}");
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
            Notify("正在重新建立基岩版游戏连接", NotificationType.Information);
        }
        catch (Exception ex)
        {
            Notify($"继续连接失败：{FriendlyError(ex)}", NotificationType.Error);
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
        try
        {
            var room = GetRoomState(Edition);
            await _client.RequestAsync(room.Role == "host" ? "room.stop" : "room.leave", RoomParameters(Edition),
                timeout: TimeSpan.FromSeconds(8), cancellationToken: _lifetime.Token);
            // The CLI is shared by the Java and Bedrock tabs. Do not restart it here:
            // restarting would invalidate the other tab's client reference and pending requests.
            ClearRoom(Edition);
            Notify("已离开房间", NotificationType.Success);
        }
        catch (Exception ex)
        {
            Notify($"退出房间失败：{FriendlyError(ex)}", NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ProbeNatAsync()
    {
        if (_client is null || !CanProbeNat) return;
        IsProbingNat = true;
        try
        {
            var response = await _client.RequestAsync("stun.probe", timeout: TimeSpan.FromSeconds(15),
                cancellationToken: _lifetime.Token);
            var udp = response.Data.TryGetProperty("udp_nat_type", out var udpValue) ? udpValue.GetInt32() : 0;
            var tcp = response.Data.TryGetProperty("tcp_nat_type", out var tcpValue) ? tcpValue.GetInt32() : 0;
            var ip = response.Data.TryGetProperty("public_ip", out var ipValue) &&
                     ipValue.ValueKind == JsonValueKind.Array
                ? string.Join(", ", ipValue.EnumerateArray().Select(item => item.GetString()))
                : "未知";
            NatSummary = $"公网地址：{ip}·UDP：{NatName(udp)}·TCP：{NatName(tcp)}";
            Notify("NAT 检测完成", NotificationType.Success);
        }
        catch (Exception ex)
        {
            Notify($"NAT 检测失败：{FriendlyError(ex)}", NotificationType.Error);
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
                case "paperconnect.room.info": ApplyRoomData(e.Data, _bedrockRoom.Role, MinecraftEdition.Bedrock); break;
                case "room.closed":
                case "room.disconnected":
                    ClearRoom(MinecraftEdition.Java);
                    Notify("房间连接已关闭", NotificationType.Information);
                    break;
                case "paperconnect.room.closed":
                case "paperconnect.room.disconnected":
                case "paperconnect.connection.closed":
                case "paperconnect.connection.disconnected":
                    ClearRoom(MinecraftEdition.Bedrock);
                    Notify("房间连接已关闭", NotificationType.Information);
                    break;
                case "paperconnect.connection.ready":
                    IsBedrockPortBusy = false;
                    Notify("游戏连接已就绪，请在基岩版局域网列表中加入", NotificationType.Success);
                    break;
                case "paperconnect.connection.port_busy":
                    IsBedrockPortBusy = true;
                    Notify("UDP 7551 被 Minecraft 占用，请关闭游戏后点击继续连接", NotificationType.Warning);
                    break;
                case "paperconnect.connection.error":
                    ClearRoom(MinecraftEdition.Bedrock);
                    Notify("基岩版游戏连接建立失败", NotificationType.Error);
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
            var response = await _client.RequestAsync("room.status", RoomParameters(targetEdition), timeout: TimeSpan.FromSeconds(4),
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
        catch when (_lifetime.IsCancellationRequested)
        {
        }
        catch
        {
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
        {
            foreach (var player in players.EnumerateArray())
            {
                var name = GetString(player, "name") ?? GetString(player, "player") ?? "未知成员";
                var vendor = GetString(player, "vendor") ?? GetString(player, "clientId") ?? string.Empty;
                var kind = GetString(player, "kind") ??
                           (player.TryGetProperty("isRoomHost", out var host) && host.ValueKind == JsonValueKind.True
                               ? "HOST"
                               : "GUEST");
                room.Members.Add(new OnlineMember { Name = name, Vendor = vendor, Kind = kind });
            }
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
        LanServers.Add(new LanServerEntry { Motd = GetString(data, "motd") ?? "Minecraft 世界", Ip = ip, Port = port });
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
        Notify("请输入联机用户名", NotificationType.Warning);
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

    private RoomState GetRoomState(MinecraftEdition edition) =>
        edition == MinecraftEdition.Java ? _javaRoom : _bedrockRoom;

    private static object RoomParameters(MinecraftEdition edition) => edition == MinecraftEdition.Bedrock
        ? new { protocol = "paperconnect" }
        : new { };

    private void ApplyActiveRoomState()
    {
        var room = GetRoomState(Edition);
        CurrentRoomCode = room.Code;
        IsInRoom = !string.IsNullOrWhiteSpace(room.Code);
        Members.Clear();
        foreach (var member in room.Members) Members.Add(member);
        OnPropertyChanged(nameof(MemberCountText));
    }

    private sealed class RoomState
    {
        public string Role { get; set; } = "none";
        public string Code { get; set; } = string.Empty;
        public List<OnlineMember> Members { get; } = [];
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string FriendlyError(Exception exception) =>
        exception is GravityConeException { Code: "INTERNAL_ERROR" } &&
        exception.Message.Contains("未检测", StringComparison.Ordinal)
            ? "未检测到已开放的 Minecraft 世界"
            : exception.Message;

    private static string NatName(int type) => type switch
    {
        1 => "开放网络", 2 => "对称防火墙", 3 => "完全圆锥", 4 => "受限圆锥",
        5 => "端口受限", 6 => "对称递增", 7 => "对称 NAT", _ => "未知"
    };

    [RelayCommand]
    private void ClearJoinCode() => JoinCode = string.Empty;

    private static void Notify(string message, NotificationType type)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: { } window
            })
            NotificationGateway.Notice(window, message, type);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        IsBackendReady = false;
        if (_client is not null)
        {
            _client.EventReceived -= ClientOnEventReceived;
        }

        _lifetime.Dispose();
    }
}

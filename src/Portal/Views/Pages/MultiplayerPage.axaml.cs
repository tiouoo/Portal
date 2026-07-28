using System.Collections.ObjectModel;
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
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages;

[DefaultPage("联机")]
[AggregatedSearchPage("联机", "联机", "Multiplayer")]
public partial class MultiplayerPage : UserControl, ITioTabPage
{
    public MultiplayerPage()
    {
        InitializeComponent();
        ViewModel = new MultiplayerPageViewModel();
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

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();
    }

    private async void CopyRoomCode_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (string.IsNullOrWhiteSpace(ViewModel.CurrentRoomCode) || topLevel?.Clipboard is not { } clipboard)
            return;
        await clipboard.SetTextAsync(ViewModel.CurrentRoomCode);
        NotificationGateway.Notice(topLevel, "房间码已复制", NotificationType.Success);
    }

    private async void PasteJoinCode_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        ViewModel.JoinCode = await clipboard.TryGetTextAsync() ?? string.Empty;
    }

    private async void EnterJavaPort_OnClick(object? sender, RoutedEventArgs e)
    {
        var result = await OverlayDialog.ShowCustomAsync<MultiplayerPortDialog, MultiplayerPortDialogViewModel, string>(
            new MultiplayerPortDialogViewModel(ViewModel.ManualJavaPort), this.GetTopLevel().TryGetHostId(),
            new OverlayDialogOptions
            {
                Title = "导入基岩版包", Buttons = DialogButton.None, CanLightDismiss = false, CanResize = false
            });
        if (result is not null) ViewModel.ManualJavaPort = result;
    }

    public void OnClose()
    {
        Loaded -= OnLoaded;
        _ = ViewModel.DisposeAsync();
        DataContext = null;
    }
}

public partial class MultiplayerPageViewModel : ObservableObject, IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private GravityConeClient? _client;
    private bool _disposed;
    private MinecraftEdition _edition = MinecraftEdition.Java;
    private string _roomRole = "none";

    public MultiplayerPageViewModel()
    {
        PlayerName = string.IsNullOrWhiteSpace(Data.ConfigEntry.OnlinePlayerName)
            ? Data.ConfigEntry.UsingMinecraftMinecraftAccount?.Name ?? string.Empty
            : Data.ConfigEntry.OnlinePlayerName;
    }

    public bool IsEditionNavigationVisible => OperatingSystem.IsWindows();
    public bool IsJava => _edition == MinecraftEdition.Java;
    public bool IsBedrock => _edition == MinecraftEdition.Bedrock;
    public string EditionTitle => IsJava ? "Java 联机" : "基岩联机";
    public bool IsNotBusy => !IsBusy;
    public bool CanOperate => IsReady && !IsBusy;
    public bool IsNotInRoom => !IsInRoom;
    public bool CanCreateRoom => CanOperate && (IsBedrock || ResolveJavaPort() is not null);
    public bool HasNatSummary => !string.IsNullOrWhiteSpace(NatSummary);
    public string MemberCountText => $"{Members.Count} 人";

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
    public partial bool IsComponentMissing { get; set; } = true;

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
    public string BedrockDiscoveryButtonText => IsDiscoveringBedrockServers ? "检测中" : "刷新";
    public string NatProbeButtonText => IsProbingNat ? "检测中" : "检测 NAT";
    public bool CanProbeNat => IsReady && !IsBusy && !IsProbingNat;

    [ObservableProperty] public partial string StatusText { get; set; } = "正在检测联机组件";
    [ObservableProperty] public partial string PlayerName { get; set; }
    [ObservableProperty] public partial string JoinCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string ManualJavaPort { get; set; } = string.Empty;
    [ObservableProperty] public partial string CurrentRoomCode { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsBedrockPortBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotInRoom))]
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
    [NotifyPropertyChangedFor(nameof(BedrockDiscoveryButtonText))]
    public partial bool IsDiscoveringBedrockServers { get; set; }

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
        var installation = GravityConeInstaller.FindInstalled();
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
        _client ??= new GravityConeClient();
        _client.EventReceived -= ClientOnEventReceived;
        _client.EventReceived += ClientOnEventReceived;
        await _client.StartAsync(installation, _lifetime.Token);
        IsBackendReady = true;
        StatusText = string.Empty;
        await RefreshRoomStatusAsync();
        if (IsJava) await DiscoverJavaServersAsync();
    }

    [RelayCommand]
    private async Task SelectJavaAsync()
    {
        if (_edition == MinecraftEdition.Java) return;
        if (IsInRoom)
        {
            Notify("请先关闭或离开当前房间", NotificationType.Warning);
            return;
        }

        _edition = MinecraftEdition.Java;
        RaiseEditionProperties();
        if (_client is not null) await DiscoverJavaServersAsync();
    }

    [RelayCommand]
    private void SelectBedrock()
    {
        if (!OperatingSystem.IsWindows() || _edition == MinecraftEdition.Bedrock) return;
        if (IsInRoom)
        {
            Notify("请先关闭或离开当前房间", NotificationType.Warning);
            return;
        }

        _edition = MinecraftEdition.Bedrock;
        RaiseEditionProperties();
    }

    private void RaiseEditionProperties()
    {
        OnPropertyChanged(nameof(IsJava));
        OnPropertyChanged(nameof(IsBedrock));
        OnPropertyChanged(nameof(EditionTitle));
        OnPropertyChanged(nameof(CanCreateRoom));
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
    private async Task DiscoverBedrockServersAsync()
    {
        if (_client is null || IsBusy || IsDiscoveringBedrockServers) return;
        IsDiscoveringBedrockServers = true;
        try
        {
            await _client.RequestAsync("lan.start_discovery", cancellationToken: _lifetime.Token);
            await Task.Delay(TimeSpan.FromSeconds(3), _lifetime.Token);
        }
        catch (Exception ex)
        {
            Notify($"基岩版局域网世界刷新失败：{FriendlyError(ex)}", NotificationType.Error);
        }
        finally
        {
            IsDiscoveringBedrockServers = false;
        }
    }

    [RelayCommand]
    private async Task CreateRoomAsync()
    {
        if (_client is null || !CanCreateRoom || !ValidatePlayerName()) return;
        IsBusy = true;
        try
        {
            object parameters = IsJava
                ? new { mc_port = ResolveJavaPort()!.Value, player_name = PlayerName.Trim() }
                : new { player_name = PlayerName.Trim(), protocol = "paperconnect" };
            var response = await _client.RequestAsync("room.create", parameters,
                timeout: TimeSpan.FromSeconds(35), cancellationToken: _lifetime.Token);
            ApplyRoomData(response.Data, "host");
            Notify("房间已创建", NotificationType.Success);
        }
        catch (Exception ex)
        {
            Notify($"创建失败：{FriendlyError(ex)}", NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task JoinRoomAsync()
    {
        if (_client is null || IsBusy || !ValidatePlayerName()) return;
        var code = JoinCode.Trim().ToUpperInvariant();
        var prefix = IsJava ? "U/" : "P/";
        if (!code.StartsWith(prefix, StringComparison.Ordinal))
        {
            Notify($"{EditionTitle}房间码必须以 {prefix} 开头", NotificationType.Warning);
            return;
        }

        IsBusy = true;
        try
        {
            var progress = new Progress<GravityConeProgress>(_ => { });
            var response = await _client.RequestAsync("room.join", new { code, player_name = PlayerName.Trim() },
                progress, TimeSpan.FromSeconds(60), _lifetime.Token);
            ApplyRoomData(response.Data, "guest");
            Notify(IsBedrock ? "控制通道已连接，正在建立游戏连接" : "已加入房间", NotificationType.Success);
        }
        catch (Exception ex)
        {
            Notify($"加入失败：{FriendlyError(ex)}", NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
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
            await _client.RequestAsync(_roomRole == "host" ? "room.stop" : "room.leave",
                timeout: TimeSpan.FromSeconds(8), cancellationToken: _lifetime.Token);
            ClearRoom();
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
        Dispatcher.UIThread.Post(() =>
        {
            switch (e.Name)
            {
                case "lan.server_found": AddLanServer(e.Data); break;
                case "lan.server_lost": RemoveLanServer(e.Data); break;
                case "room.player_joined":
                case "room.player_left":
                case "room.guest_player_list_updated":
                case "paperconnect.room.player_joined":
                case "paperconnect.room.player_left":
                    _ = RefreshRoomStatusAsync();
                    break;
                case "paperconnect.room.info": ApplyRoomData(e.Data, _roomRole); break;
                case "room.closed":
                case "room.disconnected":
                case "paperconnect.room.closed":
                case "paperconnect.room.disconnected":
                    ClearRoom();
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
                case "paperconnect.connection.error": Notify("基岩版游戏连接建立失败", NotificationType.Error); break;
            }
        });
    }

    private async Task RefreshRoomStatusAsync()
    {
        if (_client is null) return;
        try
        {
            var response = await _client.RequestAsync("room.status", timeout: TimeSpan.FromSeconds(4),
                cancellationToken: _lifetime.Token);
            var role = response.Data.TryGetProperty("role", out var roleValue)
                ? roleValue.GetString() ?? "none"
                : _roomRole;
            if (role == "none")
            {
                ClearRoom();
                return;
            }

            ApplyRoomData(response.Data, role);
        }
        catch when (_lifetime.IsCancellationRequested)
        {
        }
        catch
        {
        }
    }

    private void ApplyRoomData(JsonElement data, string role)
    {
        _roomRole = role;
        CurrentRoomCode = GetString(data, "code") ?? GetString(data, "room_code") ?? CurrentRoomCode;
        IsInRoom = !string.IsNullOrWhiteSpace(CurrentRoomCode);
        Members.Clear();
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
                Members.Add(new OnlineMember { Name = name, Vendor = vendor, Kind = kind });
            }
        }

        OnPropertyChanged(nameof(MemberCountText));
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

    private bool ValidatePlayerName()
    {
        if (!string.IsNullOrWhiteSpace(PlayerName)) return true;
        Notify("请输入联机用户名", NotificationType.Warning);
        return false;
    }

    private void ClearRoom()
    {
        _roomRole = "none";
        IsBedrockPortBusy = false;
        CurrentRoomCode = string.Empty;
        IsInRoom = false;
        Members.Clear();
        OnPropertyChanged(nameof(MemberCountText));
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
            await _client.DisposeAsync();
        }

        _lifetime.Dispose();
    }
}
using Avalonia.Media;
using Avalonia.Threading;
using Portal.Core.Minecraft.Services;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Views.Widgets;

public enum ServerPingState
{
    Unknown,
    Pinging,
    Online,
    Offline
}

/// <summary>
/// 服务器状态模型。显示方式与「我的 Minecraft 实例详情」的服务器列表完全一致：
/// 状态小圆点 + 状态文本（检测中 / 在线 / 无法连接）+ 延迟（XX ms）+ 在线人数（N / M 人）。
/// </summary>
public sealed class ServerPing
{
    private static readonly SemaphoreSlim PingGate = new(5);
    private static readonly IBrush PendingBrush = new SolidColorBrush(Color.Parse("#8C8C8C"));
    private static readonly IBrush OnlineBrush = new SolidColorBrush(Color.Parse("#52C41A"));
    private static readonly IBrush OfflineBrush = new SolidColorBrush(Color.Parse("#F5222D"));
    private static readonly IBrush PingGoodBrush = new SolidColorBrush(Color.Parse("#52C41A"));
    private static readonly IBrush PingFairBrush = new SolidColorBrush(Color.Parse("#FAAD14"));
    private static readonly IBrush PingPoorBrush = new SolidColorBrush(Color.Parse("#F5222D"));

    private readonly MinecraftServerPingService _pingService = new();
    private CancellationTokenSource? _cts;
    private ServerPingState _state;
    private long _latency;
    private int _onlinePlayers;
    private int _maxPlayers;

    public event Action? Changed;

    public ServerPingState State => _state;

    /// <summary>状态文本：未检测 / 检测中 / 在线 / 无法连接。</summary>
    public string StatusText => _state switch
    {
        ServerPingState.Pinging => "检测中",
        ServerPingState.Online => "在线",
        ServerPingState.Offline => "无法连接",
        _ => "未检测"
    };

    /// <summary>状态小圆点与状态文本的颜色：灰 / 绿 / 红。</summary>
    public IBrush StatusBrush => _state switch
    {
        ServerPingState.Online => OnlineBrush,
        ServerPingState.Offline => OfflineBrush,
        _ => PendingBrush
    };

    /// <summary>延迟文本（仅在线时显示）。</summary>
    public string PingText => _state == ServerPingState.Online ? $"{_latency} ms" : string.Empty;

    public bool HasPing => _state == ServerPingState.Online;

    /// <summary>延迟颜色：&lt;100ms 绿，&lt;300ms 黄，其余红。</summary>
    public IBrush PingBrush => _state switch
    {
        ServerPingState.Online when _latency < 100 => PingGoodBrush,
        ServerPingState.Online when _latency < 300 => PingFairBrush,
        ServerPingState.Online => PingPoorBrush,
        _ => PingGoodBrush
    };

    /// <summary>在线人数文本（如 34 / 100 人），仅在在线且有上报人数时显示。</summary>
    public string PlayersText => HasPlayers ? $"{_onlinePlayers} / {_maxPlayers} 人" : string.Empty;

    public bool HasPlayers { get; private set; }

    /// <summary>拼接「主机[:端口]」地址，IPv6 地址自动加方括号。</summary>
    public static string BuildAddress(string host, int port) =>
        host.Contains(':') ? $"[{host}]:{port}" : $"{host}:{port}";

    /// <summary>显示用地址，默认端口（25565）省略端口号。</summary>
    public static string BuildDisplayAddress(string host, int port) =>
        port == 25565 ? host : BuildAddress(host, port);

    /// <summary>启动探测。address 需为「主机[:端口]」形式的可解析地址。</summary>
    public void Start(string address)
    {
        Cancel();

        if (string.IsNullOrWhiteSpace(address))
        {
            SetState(ServerPingState.Offline);
            return;
        }

        var cts = new CancellationTokenSource();
        _cts = cts;
        SetState(ServerPingState.Pinging);
        _ = PingAsync(address, cts.Token, cts);
    }

    public void Cancel()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _cts = null;
    }

    private async Task PingAsync(string address, CancellationToken cancellationToken, CancellationTokenSource owner)
    {
        try
        {
            await PingGate.WaitAsync(cancellationToken);
            try
            {
                var status = await _pingService.PingAsync(address, cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested || !ReferenceEquals(_cts, owner))
                    return;
                if (status == null)
                    SetState(ServerPingState.Offline);
                else
                    SetState(ServerPingState.Online, status.Latency, status.OnlinePlayers, status.MaxPlayers);
            }
            finally
            {
                PingGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Logger.Warning($"检测服务器状态失败：{address}{Environment.NewLine}{exception}");
            if (!cancellationToken.IsCancellationRequested && ReferenceEquals(_cts, owner))
                SetState(ServerPingState.Offline);
        }
    }

    private void SetState(ServerPingState state, long latency = 0, int onlinePlayers = 0, int maxPlayers = 0)
    {
        _state = state;
        _latency = latency;
        if (state == ServerPingState.Online)
        {
            _onlinePlayers = onlinePlayers;
            _maxPlayers = maxPlayers;
            HasPlayers = _maxPlayers > 0 || _onlinePlayers > 0;
        }
        else
        {
            _onlinePlayers = 0;
            _maxPlayers = 0;
            HasPlayers = false;
        }

        if (Changed is not { } changed)
            return;

        Dispatcher.UIThread.Post(() => changed());
    }
}
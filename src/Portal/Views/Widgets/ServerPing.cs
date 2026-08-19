using Avalonia.Media;
using Avalonia.Threading;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Views.Widgets;

public enum ServerPingState
{
    Unknown,
    Pinging,
    Online,
    Offline
}

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
    private long _latency;
    private int _maxPlayers;
    private int _onlinePlayers;

    public ServerPingState State { get; private set; }

    public string StatusText => State switch
    {
        ServerPingState.Pinging => CommonLanguageManager.Instance.servers_pinging.CurrentValue(),
        ServerPingState.Online => CommonLanguageManager.Instance.servers_online.CurrentValue(),
        ServerPingState.Offline => CommonLanguageManager.Instance.servers_cannotConnect.CurrentValue(),
        _ => CommonLanguageManager.Instance.servers_statusUnknown.CurrentValue()
    };

    public IBrush StatusBrush => State switch
    {
        ServerPingState.Online => OnlineBrush,
        ServerPingState.Offline => OfflineBrush,
        _ => PendingBrush
    };

    public string PingText => State == ServerPingState.Online ? $"{_latency} ms" : string.Empty;

    public bool HasPing => State == ServerPingState.Online;

    public IBrush PingBrush => State switch
    {
        ServerPingState.Online when _latency < 100 => PingGoodBrush,
        ServerPingState.Online when _latency < 300 => PingFairBrush,
        ServerPingState.Online => PingPoorBrush,
        _ => PingGoodBrush
    };

    public string PlayersText => HasPlayers
        ? string.Format(CommonLanguageManager.Instance.servers_players.CurrentValue(), _onlinePlayers, _maxPlayers)
        : string.Empty;

    public bool HasPlayers { get; private set; }

    public event Action? Changed;

    public static string BuildAddress(string host, int port)
    {
        return host.Contains(':') ? $"[{host}]:{port}" : $"{host}:{port}";
    }

    public static string BuildDisplayAddress(string host, int port)
    {
        return port == 25565 ? host : BuildAddress(host, port);
    }

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
            Logger.Warning(string.Format(LogLanguageManager.Instance.servers_pingFailed.CurrentValue(), address,
                Environment.NewLine, exception));
            if (!cancellationToken.IsCancellationRequested && ReferenceEquals(_cts, owner))
                SetState(ServerPingState.Offline);
        }
    }

    private void SetState(ServerPingState state, long latency = 0, int onlinePlayers = 0, int maxPlayers = 0)
    {
        State = state;
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
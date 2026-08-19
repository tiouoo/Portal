using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Services;

public sealed record BedrockServerStatus(
    string Edition,
    string Motd,
    string Version,
    int OnlinePlayers,
    int MaxPlayers,
    long Latency);

public sealed class BedrockServerPingService
{
    private const int DefaultTimeoutMs = 4000;


    private static readonly byte[] Magic =
        [0x00, 0xff, 0xff, 0x00, 0xfe, 0xfe, 0xfe, 0xfe, 0xfd, 0xfd, 0xfd, 0xfd, 0x12, 0x34, 0x56, 0x78];

    public async Task<BedrockServerStatus?> PingAsync(string address, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;

        var (host, port) = BedrockServerManager.ParseAddress(address);
        if (port is < 1 or > 65535)
            return null;

        var endpoint = await ResolveAsync(host, port, cancellationToken).ConfigureAwait(false);
        if (endpoint == null)
            return null;

        return await PingAsync(endpoint, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BedrockServerStatus?> PingAsync(IPEndPoint endpoint,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(DefaultTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var token = linkedCts.Token;

        using var udp = new UdpClient(endpoint.AddressFamily == AddressFamily.InterNetworkV6
            ? AddressFamily.InterNetworkV6
            : AddressFamily.InterNetwork);

        try
        {
            var sentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await udp.SendAsync(BuildUnconnectedPing(sentAt), endpoint).AsTask().WaitAsync(token).ConfigureAwait(false);
            var result = await udp.ReceiveAsync(token).AsTask().WaitAsync(token).ConfigureAwait(false);
            var latency = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - sentAt;
            return ParseUnconnectedPong(result.Buffer, latency);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Debug(string.Format(LogLanguageManager.Instance.serverPing_bedrockDetectionFailed.CurrentValue(), endpoint, Environment.NewLine + exception));
            return null;
        }
    }

    private static BedrockServerStatus? ParseUnconnectedPong(byte[] data, long latency)
    {
        if (data.Length < 33 || data[0] != 0x1c)
            return null;

        if (!data.AsSpan(17, 16).SequenceEqual(Magic))
            return null;

        if (data.Length < 33 + 2)
            return null;

        var bodyLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(33, 2));
        var bodyOffset = 35;
        if (data.Length < bodyOffset + bodyLength)
            return null;

        var body = Encoding.UTF8.GetString(data, bodyOffset, bodyLength);
        return ParseServerBody(body, Math.Max(0, latency));
    }

    private static BedrockServerStatus? ParseServerBody(string body, long latency)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        var parts = body.Split(';');
        if (parts.Length < 2)
            return null;

        var edition = parts[0].Trim();
        var motd = parts[1];
        var version = parts.Length > 3 ? parts[3].Trim() : string.Empty;
        var online = parts.Length > 4 && int.TryParse(parts[4], out var onlineCount) ? onlineCount : 0;
        var max = parts.Length > 5 && int.TryParse(parts[5], out var maxCount) ? maxCount : 0;

        return new BedrockServerStatus(edition, motd.Length > 200 ? motd[..200] : motd,
            version, online, max, latency);
    }

    private static byte[] BuildUnconnectedPing(long timestamp)
    {
        var buffer = new byte[33];
        buffer[0] = 0x01;
        BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(1, 8), timestamp);
        Magic.CopyTo(buffer.AsSpan(9));

        BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(25, 8), 0);
        return buffer;
    }

    private static async Task<IPEndPoint?> ResolveAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
            return new IPEndPoint(literal, port);

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            if (addresses.Length == 0)
                return null;

            var target = addresses.FirstOrDefault(item => item.AddressFamily == AddressFamily.InterNetwork)
                         ?? addresses[0];
            return new IPEndPoint(target, port);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Warning(string.Format(LogLanguageManager.Instance.serverPing_bedrockResolveFailed.CurrentValue(), host, Environment.NewLine + exception));
            return null;
        }
    }
}
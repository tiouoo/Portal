using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Services;

/// <summary>
/// 服务器状态查询结果。
/// </summary>
public sealed record MinecraftServerStatus(
    string Description,
    string Version,
    int OnlinePlayers,
    int MaxPlayers,
    long Latency,
    string? Favicon);

/// <summary>
/// Minecraft 服务器状态查询服务，支持现代（1.7+）与旧版（1.6-）探测协议。
/// 实现思路参考 PCLCE 的 McPingService / LegacyMcPingService。
/// </summary>
public sealed class MinecraftServerPingService
{
    private const int DefaultTimeoutMs = 8000;

    /// <summary>
    /// 查询服务器状态，失败或超时返回 null。
    /// </summary>
    public async Task<MinecraftServerStatus?> PingAsync(string address, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;

        var (host, port) = JavaServerManager.ParseAddress(address);
        if (port is < 1 or > 65535)
            return null;

        using var timeoutCts = new CancellationTokenSource(DefaultTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var token = linkedCts.Token;

        var endpoint = await ResolveAsync(host, port, token).ConfigureAwait(false);
        if (endpoint == null)
            return null;

        var modern = await PingModernAsync(host, endpoint, token).ConfigureAwait(false);
        if (modern != null)
            return modern;

        return await PingLegacyAsync(endpoint, token).ConfigureAwait(false);
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
            Logger.Warning($"解析服务器地址失败：{host}{Environment.NewLine}{exception}");
            return null;
        }
    }

    private static async Task<MinecraftServerStatus?> PingModernAsync(string host, IPEndPoint endpoint,
        CancellationToken cancellationToken)
    {
        using var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }

        await using var stream = new NetworkStream(socket, false);
        try
        {
            await stream.WriteAsync(BuildHandshakePacket(host, endpoint.Port), cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(new ReadOnlyMemory<byte>(new byte[] { 0x01, 0x00 }), cancellationToken)
                .ConfigureAwait(false);

            var statusPayload = await ReadStatusPayloadAsync(stream, cancellationToken).ConfigureAwait(false);
            if (statusPayload is null or { Length: 0 })
                return null;

            var pingTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await stream.WriteAsync(BuildPingRequestPacket(pingTimestamp), cancellationToken).ConfigureAwait(false);
            var latency = await ReadPongLatencyAsync(stream, pingTimestamp, cancellationToken).ConfigureAwait(false);

            return ParseStatusPayload(statusPayload, latency);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<MinecraftServerStatus?> PingLegacyAsync(IPEndPoint endpoint,
        CancellationToken cancellationToken)
    {
        using var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }

        try
        {
            await using var stream = new NetworkStream(socket, false);
            await stream.WriteAsync(new ReadOnlyMemory<byte>(new byte[] { 0xfe, 0x01 }), cancellationToken)
                .ConfigureAwait(false);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            var data = ms.ToArray();
            if (data.Length < 21 || data[0] != 0xff)
                return null;

            var text = Encoding.BigEndianUnicode.GetString(data, 1, data.Length - 1);
            // 旧版格式：§1\0{protocol}\0{motd}\0{online}\0{max}，可能带 3 个尾部 0x07
            var parts = text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
                return null;

            var motd = parts[1];
            var online = int.TryParse(parts[2], out var onlineCount) ? onlineCount : 0;
            var max = int.TryParse(parts[3], out var maxCount) ? maxCount : 0;
            return new MinecraftServerStatus(motd, string.Empty, online, max, 0, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<byte[]?> ReadStatusPayloadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var packetLength = checked((int)await ReadVarIntAsync(stream, cancellationToken).ConfigureAwait(false));
        if (packetLength <= 0)
            return null;

        var packetData = await ReadExactAsync(stream, packetLength, cancellationToken).ConfigureAwait(false);
        using var packetStream = new MemoryStream(packetData, writable: false);
        var packetId = checked((int)await ReadVarIntAsync(packetStream, cancellationToken).ConfigureAwait(false));
        if (packetId != 0)
            return null;

        var jsonLength = checked((int)await ReadVarIntAsync(packetStream, cancellationToken).ConfigureAwait(false));
        return await ReadExactAsync(packetStream, jsonLength, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<long> ReadPongLatencyAsync(NetworkStream stream, long pingTimestamp,
        CancellationToken cancellationToken)
    {
        var packetLength = checked((int)await ReadVarIntAsync(stream, cancellationToken).ConfigureAwait(false));
        if (packetLength != 9)
            return 0;

        var packetData = await ReadExactAsync(stream, packetLength, cancellationToken).ConfigureAwait(false);
        using var packetStream = new MemoryStream(packetData, writable: false);
        var packetId = checked((int)await ReadVarIntAsync(packetStream, cancellationToken).ConfigureAwait(false));
        if (packetId != 1)
            return 0;

        var pong = await ReadExactAsync(packetStream, 8, cancellationToken).ConfigureAwait(false);
        var pongPayload = BinaryPrimitives.ReadInt64BigEndian(pong);
        return Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - pongPayload);
    }

    private static MinecraftServerStatus? ParseStatusPayload(byte[] payload, long latency)
    {
        try
        {
            var root = JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject;
            if (root == null)
                return null;

            var description = ToPlainText(root["description"]);
            var version = (root["version"] as JsonObject)?["name"]?.GetValue<string>() ?? string.Empty;

            var online = 0;
            var max = 0;
            if (root["players"] is JsonObject players)
            {
                online = players["online"]?.GetValue<int>() ?? 0;
                max = players["max"]?.GetValue<int>() ?? 0;
            }

            var favicon = root["favicon"]?.GetValue<string>();
            return new MinecraftServerStatus(description, version, online, max, latency, favicon);
        }
        catch (Exception exception)
        {
            Logger.Warning($"解析服务器状态响应失败。{Environment.NewLine}{exception}");
            return null;
        }
    }

    /// <summary>
    /// 将聊天组件（字符串 / 对象 / 数组）转换为纯文本 MOTD。
    /// </summary>
    private static string ToPlainText(JsonNode? node)
    {
        if (node == null)
            return string.Empty;

        var sb = new StringBuilder();
        var stack = new Stack<JsonNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            switch (current.GetValueKind())
            {
                case JsonValueKind.Object:
                {
                    var obj = current.AsObject();
                    if (obj.TryGetPropertyValue("extra", out var extra) && extra is JsonArray extraArray)
                    {
                        for (var i = extraArray.Count - 1; i >= 0; i--)
                            if (extraArray[i] is { } item)
                                stack.Push(item);
                    }

                    if (obj.TryGetPropertyValue("text", out var text))
                        sb.Append(text?.GetValue<string>() ?? string.Empty);
                    else if (obj.TryGetPropertyValue("translate", out var translate))
                        sb.Append(translate?.GetValue<string>() ?? string.Empty);
                    break;
                }
                case JsonValueKind.String:
                    sb.Append(current.GetValue<string>());
                    break;
                case JsonValueKind.Array:
                {
                    var array = current.AsArray();
                    for (var i = array.Count - 1; i >= 0; i--)
                        if (array[i] is { } item)
                            stack.Push(item);
                    break;
                }
            }
        }

        return sb.ToString();
    }

    // ===== 现代协议数据包构建 =====

    private static byte[] BuildHandshakePacket(string host, int port)
    {
        var body = new List<byte>();
        body.AddRange(EncodeVarInt(0));
        body.AddRange(EncodeVarInt(772));
        var hostBytes = Encoding.UTF8.GetBytes(host);
        if (hostBytes.Length > 255)
            hostBytes = hostBytes[..255];
        body.AddRange(EncodeVarInt(hostBytes.Length));
        body.AddRange(hostBytes);
        body.AddRange(BitConverter.GetBytes((ushort)port).Reverse());
        body.AddRange(EncodeVarInt(1));

        var packet = new List<byte>();
        packet.AddRange(EncodeVarInt(body.Count));
        packet.AddRange(body);
        return packet.ToArray();
    }

    private static byte[] BuildPingRequestPacket(long timestamp)
    {
        var body = new List<byte>();
        body.AddRange(EncodeVarInt(1));
        body.AddRange(BitConverter.GetBytes(timestamp).Reverse());

        var packet = new List<byte>();
        packet.AddRange(EncodeVarInt(body.Count));
        packet.AddRange(body);
        return packet.ToArray();
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer;
    }

    // ===== VarInt =====

    private static byte[] EncodeVarInt(int value)
    {
        var bytes = new List<byte>();
        var remaining = (uint)value;
        do
        {
            var next = (byte)(remaining & 0x7F);
            remaining >>= 7;
            if (remaining != 0)
                next |= 0x80;
            bytes.Add(next);
        } while (remaining != 0);

        return bytes.ToArray();
    }

    private static async Task<uint> ReadVarIntAsync(Stream stream, CancellationToken cancellationToken)
    {
        uint result = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            var buffer = new byte[1];
            await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            var next = buffer[0];
            result |= (uint)(next & 0x7F) << shift;
            if ((next & 0x80) == 0)
                return result;
        }

        throw new InvalidDataException("VarInt 长度超出限制。");
    }
}

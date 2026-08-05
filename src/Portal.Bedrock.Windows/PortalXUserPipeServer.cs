using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Portal.Bedrock;

internal sealed class PortalXUserPipeServer(int processId, byte[] payload, TimeSpan timeout) : IDisposable
{
    private const int HeaderSize = 80;
    private const int MaxPayloadSize = 262144;
    private readonly CancellationTokenSource _stop = new();
    private byte[] _payload = payload is { Length: > 0 and <= MaxPayloadSize }
        ? payload.ToArray()
        : throw new ArgumentOutOfRangeException(nameof(payload));

    public async Task ServeAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        linkedCancellation.CancelAfter(timeout);
        await using var pipe = new NamedPipeServerStream($"Portal.XUser.{processId}", PipeDirection.Out, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            HeaderSize + _payload.Length, 0);
        try
        {
            await pipe.WaitForConnectionAsync(linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_stop.IsCancellationRequested)
        {
            throw new TimeoutException($"XUserHook 未在 {timeout.TotalSeconds:0} 秒内连接 Portal 会话管道。");
        }

        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var clientProcessId) ||
            clientProcessId != (uint)processId)
            throw new InvalidOperationException("Portal XUser 会话管道连接者不是目标进程。");

        var now = (ulong)Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var digest = SHA256.HashData(_payload);
        var header = new byte[HeaderSize];
        try
        {
            "PORTALX1"u8.CopyTo(header);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12, 4), (uint)processId);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16, 4), (uint)Environment.ProcessId);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(24, 8), now);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(32, 8), now + 60);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40, 4), (uint)_payload.Length);
            digest.CopyTo(header, 48);
            await pipe.WriteAsync(header, linkedCancellation.Token).ConfigureAwait(false);
            await pipe.WriteAsync(_payload, linkedCancellation.Token).ConfigureAwait(false);
            await pipe.FlushAsync(linkedCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(header);
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        CryptographicOperations.ZeroMemory(Interlocked.Exchange(ref _payload, []));
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(nint pipe, out uint processId);
}

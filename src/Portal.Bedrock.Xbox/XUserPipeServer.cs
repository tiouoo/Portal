using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Portal.Bedrock.Xbox;

public class XUserPipeServer : IDisposable
{
	private struct SecurityAttributes
	{
		internal uint Length;

		internal nint SecurityDescriptor;

		internal int InheritHandle;
	}

	private const int HeaderSize = 80;

	private const int MaxPayloadSize = 262144;

	private const uint PipeAccessOutbound = 2u;

	private const uint FileFlagFirstPipeInstance = 524288u;

	private const uint PipeTypeByte = 0u;

	private const uint PipeReadModeByte = 0u;

	private const uint PipeNowait = 1u;

	private const uint PipeRejectRemoteClients = 8u;

	private const uint ErrorNoData = 232u;

	private const uint ErrorPipeConnected = 535u;

	private const uint ErrorPipeListening = 536u;

	private const uint SddlRevision1 = 1u;

	private const uint PipeVersion = 1u;

	private const ulong SessionLifetimeSeconds = 60uL;

	private nint _pipe;

	private byte[]? _payload;

	private readonly uint _targetPid;

	private readonly TimeSpan _timeout;

	private CancellationTokenSource? _cts;

	private static ReadOnlySpan<byte> PipeMagic => "BMCBLXU1"u8;

	public XUserPipeServer(int targetPid, byte[] payload, TimeSpan? timeout = null)
	{
		if (payload == null)
		{
			throw new ArgumentNullException("payload");
		}
		int num = payload.Length;
		if ((num > 262144 || num == 0) ? true : false)
		{
			throw new ArgumentOutOfRangeException("payload");
		}
		_targetPid = (uint)targetPid;
		_payload = payload;
		_timeout = timeout ?? TimeSpan.FromSeconds(15L);
		_cts = new CancellationTokenSource();
	}

	public Task StartAsync()
	{
		return Task.Run(delegate
		{
			Serve(_cts.Token);
		}, _cts.Token);
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		return Task.Run(delegate
		{
			Serve(cancellationToken);
		}, cancellationToken);
	}

	public void Prepare()
	{
		if (_pipe == 0)
		{
			CreatePipe();
		}
	}

	private unsafe void Serve(CancellationToken cancellationToken)
	{
		if (_pipe == 0)
		{
			CreatePipe();
		}
		DateTimeOffset dateTimeOffset = DateTimeOffset.UtcNow + _timeout;
		bool flag = false;
		while (!flag && DateTimeOffset.UtcNow < dateTimeOffset)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (ConnectNamedPipe(_pipe, 0) != 0)
			{
				flag = true;
				break;
			}
			uint lastPInvokeError = (uint)Marshal.GetLastPInvokeError();
			switch (lastPInvokeError)
			{
			case 232u:
			case 536u:
				if (cancellationToken.WaitHandle.WaitOne(5))
				{
					cancellationToken.ThrowIfCancellationRequested();
				}
				continue;
			default:
				throw new Win32Exception((int)lastPInvokeError, "等待 XUser 管道客户端失败。");
			case 535u:
				break;
			}
			flag = true;
			break;
		}
		if (!flag)
		{
			throw new TimeoutException($"XUser 注入组件未在 {_timeout.TotalSeconds:0} 秒内连接会话管道。");
		}
		uint num = 0u;
		if (GetNamedPipeClientProcessId(_pipe, &num) == 0 || num != _targetPid)
		{
			throw new InvalidOperationException("XUser 会话管道连接者不是目标进程。");
		}
		ulong num2 = (ulong)Math.Max(0L, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
		ulong value = num2 + 60;
		byte[] payload = _payload!;
		byte[] array = SHA256.HashData(payload);
		byte[] array2 = new byte[80];
		try
		{
			PipeMagic.CopyTo(array2.AsSpan(0, 8));
			WriteUInt32Le(array2, 8, 1u);
			WriteUInt32Le(array2, 12, _targetPid);
			WriteUInt32Le(array2, 16, GetCurrentProcessId());
			WriteUInt64Le(array2, 24, num2);
			WriteUInt64Le(array2, 32, value);
			WriteUInt32Le(array2, 40, (uint)payload.Length);
			array.CopyTo(array2, 48);
			WriteExactly(array2);
			WriteExactly(payload);
			if (FlushFileBuffers(_pipe) == 0)
			{
				uint lastWin32Error = (uint)Marshal.GetLastWin32Error();
				if (lastWin32Error != 109)
				{
					throw new Win32Exception((int)lastWin32Error, "刷新 XUser 管道失败。");
				}
			}
			DisconnectNamedPipe(_pipe);
			nint num3 = Interlocked.Exchange(ref _pipe, 0);
			if (num3 != 0 && num3 != -1)
			{
				CloseHandle(num3);
			}
		}
		finally
		{
			CryptographicOperations.ZeroMemory(array2);
			CryptographicOperations.ZeroMemory(array);
		}
	}

	private unsafe void CreatePipe()
	{
		nint num = 0;
		if (ConvertStringSecurityDescriptorToSecurityDescriptorW("D:P(A;;GA;;;SY)(A;;GA;;;OW)", 1u, &num, null) == 0 || num == 0)
		{
			throw new Win32Exception(Marshal.GetLastPInvokeError(), "创建 XUser 管道安全描述符失败。");
		}
		try
		{
			SecurityAttributes securityAttributes = new SecurityAttributes
			{
				Length = (uint)sizeof(SecurityAttributes),
				SecurityDescriptor = num,
				InheritHandle = 0
			};
			_pipe = CreateNamedPipeW($"\\\\.\\pipe\\BMCBL.XUser.{_targetPid}", 524290u, 9u, 1u, (uint)(80 + _payload.Length), 0u, 0u, &securityAttributes);
		}
		finally
		{
			LocalFree(num);
		}
		long num2 = _pipe;
		if (((ulong)(num2 - -1) <= 1uL) ? true : false)
		{
			_pipe = 0;
			throw new Win32Exception(Marshal.GetLastPInvokeError(), "创建 XUser 一次性命名管道失败。");
		}
	}

	private unsafe void WriteExactly(byte[] bytes)
	{
		uint num;
		for (int i = 0; i < bytes.Length; i += (int)num)
		{
			num = 0u;
			fixed (byte* buffer = &bytes[i])
			{
				if (WriteFile(_pipe, buffer, (uint)(bytes.Length - i), &num, 0) == 0 || num == 0)
				{
					throw new Win32Exception(Marshal.GetLastPInvokeError(), "命名管道提前关闭。");
				}
			}
		}
	}

	private static void WriteUInt32Le(Span<byte> buffer, int offset, uint value)
	{
		buffer[offset] = (byte)value;
		buffer[offset + 1] = (byte)(value >> 8);
		buffer[offset + 2] = (byte)(value >> 16);
		buffer[offset + 3] = (byte)(value >> 24);
	}

	private static void WriteUInt64Le(Span<byte> buffer, int offset, ulong value)
	{
		for (int i = 0; i < 8; i++)
		{
			buffer[offset + i] = (byte)(value >> 8 * i);
		}
	}

	public void Stop()
	{
		_cts?.Cancel();
		Dispose();
	}

	public void Dispose()
	{
		_cts?.Dispose();
		_cts = null;
		nint num = Interlocked.Exchange(ref _pipe, 0);
		if (num != 0 && num != -1)
		{
			CloseHandle(num);
		}
		if (_payload != null)
		{
			CryptographicOperations.ZeroMemory(Interlocked.Exchange(ref _payload, Array.Empty<byte>()));
		}
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
	private unsafe static extern nint CreateNamedPipeW(string name, uint openMode, uint pipeMode, uint maxInstances, uint outputBufferSize, uint inputBufferSize, uint defaultTimeout, SecurityAttributes* securityAttributes);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	private static extern int ConnectNamedPipe(nint pipe, nint overlapped);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	private static extern int DisconnectNamedPipe(nint pipe);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	private unsafe static extern int GetNamedPipeClientProcessId(nint pipe, uint* processId);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	private unsafe static extern int WriteFile(nint file, void* buffer, uint bytesToWrite, uint* bytesWritten, nint overlapped);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	private static extern int FlushFileBuffers(nint file);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	private static extern int CloseHandle(nint handle);

	[DllImport("kernel32.dll", ExactSpelling = true)]
	private static extern uint GetCurrentProcessId();

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
	private unsafe static extern int ConvertStringSecurityDescriptorToSecurityDescriptorW(string descriptor, uint revision, nint* securityDescriptor, uint* descriptorSize);

	[DllImport("kernel32.dll", ExactSpelling = true)]
	private static extern nint LocalFree(nint memory);
}

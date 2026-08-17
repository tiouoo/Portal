using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

namespace Portal.Bedrock.Hook;

internal static class XUserPipeClient
{
	private struct ProcessEntry32W
	{
		public uint Size;

		public uint Usage;

		public uint ProcessId;

		public nint DefaultHeapId;

		public uint ModuleId;

		public uint Threads;

		public uint ParentProcessId;

		public int PriorityClassBase;

		public uint Flags;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 260)]
		public ushort[] ExeFile;
	}

	private const int PipeHeaderSize = 80;

	private const int MaxPayloadSize = 262144;

	private const uint MinTokenRemainingSeconds = 30u;

	private const uint PipeVersion = 1u;

	private const uint GenericRead = 2147483648u;

	private const uint OpenExisting = 3u;

	private const uint FileAttributeNormal = 128u;

	private const uint Th32csSnapprocess = 2u;

	private const uint ErrorNoData = 232u;

	private const int ReadTimeoutMs = 15000;

	private static ReadOnlySpan<byte> PipeMagic => "BMCBLXU1"u8;

	public static byte[]? ReceiveSessionPayload()
	{
		uint currentProcessId = GetCurrentProcessId();
		nint num = OpenSessionPipe(currentProcessId);
		if (num == 0)
		{
			XUserBridge.Warn($"XUser 会话管道不可用（{currentProcessId}）；不安装 QueryApiImpl Hook");
			return null;
		}
		try
		{
			if (GetNamedPipeServerProcessId(num, out var serverProcessId) == 0)
			{
				XUserBridge.Warn("GetNamedPipeServerProcessId 失败；拒绝会话");
				return null;
			}
			uint num2 = ParentProcessId(currentProcessId);
			if (num2 == 0 || serverProcessId != num2)
			{
				XUserBridge.Warn($"管道服务进程校验失败：server={serverProcessId} parent={num2}；拒绝会话");
				return null;
			}
			byte[] array = new byte[80];
			if (!ReadExactly(num, array))
			{
				XUserBridge.Warn("读取 XUser 管道头失败");
				return null;
			}
			if (!((ReadOnlySpan<byte>)array.AsSpan(0, 8)).SequenceEqual(PipeMagic))
			{
				XUserBridge.Warn("XUser 管道头 magic 不匹配");
				return null;
			}
			uint num3 = ReadUInt32Le(array, 8);
			uint num4 = ReadUInt32Le(array, 12);
			uint num5 = ReadUInt32Le(array, 16);
			ulong num6 = ReadUInt64Le(array, 24);
			ulong num7 = ReadUInt64Le(array, 32);
			uint num8 = ReadUInt32Le(array, 40);
			byte[] array2 = array.AsSpan(48, 32).ToArray();
			if (num3 != 1 || num4 != currentProcessId || num5 != serverProcessId || num8 == 0 || num8 > 262144)
			{
				XUserBridge.Warn($"XUser 管道头字段校验失败：version={num3} target={num4} self={currentProcessId} server={num5} len={num8}");
				return null;
			}
			ulong num9 = NowEpoch();
			if (num6 > num9 + 30 || num7 <= num9 || num7 - num6 > 120)
			{
				XUserBridge.Warn($"XUser 会话时间窗校验失败：issued={num6} expires={num7} now={num9}");
				return null;
			}
			byte[] array3 = new byte[num8];
			if (!ReadExactly(num, array3))
			{
				XUserBridge.Warn("读取 XUser 会话载荷失败");
				CryptographicOperations.ZeroMemory(array3);
				return null;
			}
			byte[] array4 = SHA256.HashData(array3);
			bool flag = CryptographicOperations.FixedTimeEquals(array4, array2);
			CryptographicOperations.ZeroMemory(array4);
			CryptographicOperations.ZeroMemory(array2);
			if (!flag)
			{
				XUserBridge.Warn("XUser 会话载荷 SHA256 校验失败");
				CryptographicOperations.ZeroMemory(array3);
				return null;
			}
			return array3;
		}
		finally
		{
			CloseHandle(num);
		}
	}

	private static nint OpenSessionPipe(uint currentProcessId)
	{
		string name = $"\\\\.\\pipe\\BMCBL.XUser.{currentProcessId}";
		long deadline = Environment.TickCount64 + 20000;
		int lastError = 0;
		while (Environment.TickCount64 < deadline)
		{
			nint handle = CreateFileW(name, 2147483648u, 0u, IntPtr.Zero, 3u, 128u, IntPtr.Zero);
			if (handle != IntPtr.Zero && handle != new IntPtr(-1))
			{
				return handle;
			}
			lastError = Marshal.GetLastWin32Error();
			if (lastError is not (2 or 231 or 232))
			{
				XUserBridge.Warn($"CreateFileW 打开 XUser 管道失败 error=0x{lastError:X8}");
				return 0;
			}
			Thread.Sleep(25);
		}
		XUserBridge.Warn($"打开 XUser 管道超时（{name} error=0x{lastError:X8}）");
		return 0;
	}

	private unsafe static bool ReadExactly(nint handle, byte[] output)
	{
		int num = 0;
		long num2 = Environment.TickCount64 + 15000;
		while (num < output.Length)
		{
			int bytesToRead = Math.Min(output.Length - num, int.MaxValue);
			int num3;
			uint bytesRead;
			fixed (byte* buffer = &output[num])
			{
				num3 = ReadFile(handle, buffer, (uint)bytesToRead, out bytesRead, IntPtr.Zero);
			}
			if (num3 != 0)
			{
				if (bytesRead == 0)
				{
					return false;
				}
				num += (int)bytesRead;
				continue;
			}
			if (Marshal.GetLastWin32Error() != 232)
			{
				return false;
			}
			if (Environment.TickCount64 >= num2)
			{
				return false;
			}
			Thread.Sleep(5);
		}
		return true;
	}

	private static uint ParentProcessId(uint currentPid)
	{
		nint num = CreateToolhelp32Snapshot(2u, 0u);
		if (num == IntPtr.Zero || num == new IntPtr(-1))
		{
			return 0u;
		}
		try
		{
			ProcessEntry32W entry = new ProcessEntry32W
			{
				Size = (uint)Marshal.SizeOf<ProcessEntry32W>()
			};
			if (Process32FirstW(num, ref entry) == 0)
			{
				return 0u;
			}
			do
			{
				if (entry.ProcessId == currentPid)
				{
					return entry.ParentProcessId;
				}
			}
			while (Process32NextW(num, ref entry) != 0);
			return 0u;
		}
		finally
		{
			CloseHandle(num);
		}
	}

	private static uint ReadUInt32Le(byte[] buffer, int offset)
	{
		return (uint)(buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
	}

	private static ulong ReadUInt64Le(byte[] buffer, int offset)
	{
		ulong num = 0uL;
		for (int num2 = 7; num2 >= 0; num2--)
		{
			num = (num << 8) | buffer[offset + num2];
		}
		return num;
	}

	private static ulong NowEpoch()
	{
		return (ulong)Math.Max(0L, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
	private static extern nint CreateFileW(string name, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	private unsafe static extern int ReadFile(nint file, byte* buffer, uint bytesToRead, out uint bytesRead, nint overlapped);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	private static extern int GetNamedPipeServerProcessId(nint pipe, out uint serverProcessId);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	private static extern int Process32FirstW(nint snapshot, ref ProcessEntry32W entry);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	private static extern int Process32NextW(nint snapshot, ref ProcessEntry32W entry);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	private static extern int CloseHandle(nint handle);

	[DllImport("kernel32.dll", ExactSpelling = true)]
	private static extern uint GetCurrentProcessId();
}

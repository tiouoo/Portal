using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Portal.Bedrock.Hook.Network;

internal static class WinSock2Hook
{
	private struct Wsabuf
	{
		public uint Len;

		public nint Buf;
	}

	private static class NativeMethods
	{
		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
		internal static extern nint GetModuleHandleW(string moduleName);

		[DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
		internal static extern nint GetProcAddress(nint module, string name);
	}

	private const short AF_INET = 2;

	private static nint _socketOriginal;

	private static nint _connectOriginal;

	private static nint _sendtoOriginal;

	private static nint _recvfromOriginal;

	private static nint _wsaSendToOriginal;

	private static nint _wsaRecvFromOriginal;

	public unsafe static void Install()
	{
		if (NetworkHookConfig.EnableNetworkHooks || NetworkHookConfig.EnableP2pRedirection)
		{
			nint moduleHandleW = NativeMethods.GetModuleHandleW("ws2_32.dll");
			if (moduleHandleW == 0)
			{
				XUserBridge.Warn("ws2_32.dll 未加载，跳过网络 Hook");
				return;
			}
			int num = 0;
			num += (Hook(ref _socketOriginal, moduleHandleW, "socket", (nint)(delegate* unmanaged<int, int, int, nint>)(&HookSocket)) ? 1 : 0);
			num += (Hook(ref _connectOriginal, moduleHandleW, "connect", (nint)(delegate* unmanaged<nint, nint, int, int>)(&HookConnect)) ? 1 : 0);
			num += (Hook(ref _sendtoOriginal, moduleHandleW, "sendto", (nint)(delegate* unmanaged<nint, byte*, int, int, nint, int, int>)(&HookSendTo)) ? 1 : 0);
			num += (Hook(ref _recvfromOriginal, moduleHandleW, "recvfrom", (nint)(delegate* unmanaged<nint, byte*, int, int, nint, nint, int>)(&HookRecvFrom)) ? 1 : 0);
			num += (Hook(ref _wsaSendToOriginal, moduleHandleW, "WSASendTo", (nint)(delegate* unmanaged<nint, nint, uint, uint*, uint, nint, int, nint, nint, int>)(&HookWsaSendTo)) ? 1 : 0);
			num += (Hook(ref _wsaRecvFromOriginal, moduleHandleW, "WSARecvFrom", (nint)(delegate* unmanaged<nint, nint, uint, uint*, uint*, nint, nint, nint, nint, int>)(&HookWsaRecvFrom)) ? 1 : 0);
			XUserBridge.Info($"[net-hook] 已安装 {num}/6 个 WinSock Hook | listenPort={NetworkHookConfig.NetworkListenPort} | verbose={NetworkHookConfig.NetworkVerbose}");
		}
	}

	private static bool Hook(ref nint original, nint module, string export, nint detour)
	{
		nint procAddress = NativeMethods.GetProcAddress(module, export);
		if (procAddress == 0 || !InlineHook.TryCreate(procAddress, detour, out var trampoline))
		{
			return false;
		}
		original = trampoline;
		return true;
	}

	[UnmanagedCallersOnly(EntryPoint = "NetSocket")]
	private unsafe static nint HookSocket(int af, int type, int protocol)
	{
		return ((delegate* unmanaged<int, int, int, nint>)_socketOriginal)(af, type, protocol);
	}

	[UnmanagedCallersOnly(EntryPoint = "NetConnect")]
	private unsafe static int HookConnect(nint socket, nint name, int namelen)
	{
		if (NetworkHookConfig.EnableNetworkHooks && name != 0)
		{
			ushort num = ReadPort((byte*)name);
			if (IsInterestingPort(num))
			{
				XUserBridge.Info($"[net] connect -> {FormatAddress((byte*)name)}:{num}");
			}
		}
		return ((delegate* unmanaged<nint, nint, int, int>)_connectOriginal)(socket, name, namelen);
	}

	[UnmanagedCallersOnly(EntryPoint = "NetSendTo")]
	private unsafe static int HookSendTo(nint socket, byte* buffer, int length, int flags, nint to, int tolen)
	{
		if (to == 0)
		{
			return ((delegate* unmanaged<nint, byte*, int, int, nint, int, int>)_sendtoOriginal)(socket, buffer, length, flags, to, tolen);
		}
		ushort num = ReadPort((byte*)to);
		bool flag = NetworkHookConfig.EnableP2pRedirection && ShouldRedirectPort(num) && !IsLoopback((byte*)to);
		if (NetworkHookConfig.EnableNetworkHooks)
		{
			if (NetworkHookConfig.ShouldIgnorePort(num))
			{
				return ((delegate* unmanaged<nint, byte*, int, int, nint, int, int>)_sendtoOriginal)(socket, buffer, length, flags, to, tolen);
			}
			if (NetworkHookConfig.NetworkVerbose && num == 7551)
			{
				ProbeNetherNet(buffer, length, "send");
			}
		}
		if (!flag)
		{
			return ((delegate* unmanaged<nint, byte*, int, int, nint, int, int>)_sendtoOriginal)(socket, buffer, length, flags, to, tolen);
		}
		Span<byte> span = stackalloc byte[16];
		new ReadOnlySpan<byte>((void*)to, Math.Min(tolen, 16)).CopyTo(span);
		WriteAddress(span, NetworkHookConfig.P2pTargetIp);
		fixed (byte* ptr = span)
		{
			return ((delegate* unmanaged<nint, byte*, int, int, nint, int, int>)_sendtoOriginal)(socket, buffer, length, flags, (nint)ptr, tolen);
		}
	}

	[UnmanagedCallersOnly(EntryPoint = "NetRecvFrom")]
	private unsafe static int HookRecvFrom(nint socket, byte* buffer, int length, int flags, nint from, nint fromlen)
	{
		int num = ((delegate* unmanaged<nint, byte*, int, int, nint, nint, int>)_recvfromOriginal)(socket, buffer, length, flags, from, fromlen);
		if (NetworkHookConfig.EnableNetworkHooks && NetworkHookConfig.NetworkVerbose && num > 0 && from != 0)
		{
			ushort num2 = ReadPort((byte*)from);
			if (num2 == 7551 && !NetworkHookConfig.ShouldIgnorePort(num2))
			{
				ProbeNetherNet(buffer, num, "recv");
			}
		}
		return num;
	}

	[UnmanagedCallersOnly(EntryPoint = "NetWSASendTo")]
	private unsafe static int HookWsaSendTo(nint socket, nint lpBuffers, uint dwBufferCount, uint* lpNumberOfBytesSent, uint dwFlags, nint to, int tolen, nint lpOverlapped, nint lpCompletionRoutine)
	{
		if (to == 0)
		{
			return ((delegate* unmanaged<nint, nint, uint, uint*, uint, nint, int, nint, nint, int>)_wsaSendToOriginal)(socket, lpBuffers, dwBufferCount, lpNumberOfBytesSent, dwFlags, to, tolen, lpOverlapped, lpCompletionRoutine);
		}
		ushort num = ReadPort((byte*)to);
		bool flag = NetworkHookConfig.EnableP2pRedirection && ShouldRedirectPort(num) && !IsLoopback((byte*)to);
		if (NetworkHookConfig.EnableNetworkHooks)
		{
			if (NetworkHookConfig.ShouldIgnorePort(num))
			{
				return ((delegate* unmanaged<nint, nint, uint, uint*, uint, nint, int, nint, nint, int>)_wsaSendToOriginal)(socket, lpBuffers, dwBufferCount, lpNumberOfBytesSent, dwFlags, to, tolen, lpOverlapped, lpCompletionRoutine);
			}
			if (NetworkHookConfig.NetworkVerbose && num == 7551 && lpBuffers != 0)
			{
				ProbeNetherNet((byte*)((Wsabuf*)lpBuffers)->Buf, (int)((Wsabuf*)lpBuffers)->Len, "wsasend");
			}
		}
		if (!flag)
		{
			return ((delegate* unmanaged<nint, nint, uint, uint*, uint, nint, int, nint, nint, int>)_wsaSendToOriginal)(socket, lpBuffers, dwBufferCount, lpNumberOfBytesSent, dwFlags, to, tolen, lpOverlapped, lpCompletionRoutine);
		}
		Span<byte> span = stackalloc byte[16];
		new ReadOnlySpan<byte>((void*)to, Math.Min(tolen, 16)).CopyTo(span);
		WriteAddress(span, NetworkHookConfig.P2pTargetIp);
		fixed (byte* ptr = span)
		{
			return ((delegate* unmanaged<nint, nint, uint, uint*, uint, nint, int, nint, nint, int>)_wsaSendToOriginal)(socket, lpBuffers, dwBufferCount, lpNumberOfBytesSent, dwFlags, (nint)ptr, tolen, lpOverlapped, lpCompletionRoutine);
		}
	}

	[UnmanagedCallersOnly(EntryPoint = "NetWSARecvFrom")]
	private unsafe static int HookWsaRecvFrom(nint socket, nint lpBuffers, uint dwBufferCount, uint* lpNumberOfBytesRecvd, uint* lpFlags, nint from, nint fromlen, nint lpOverlapped, nint lpCompletionRoutine)
	{
		int num = ((delegate* unmanaged<nint, nint, uint, uint*, uint*, nint, nint, nint, nint, int>)_wsaRecvFromOriginal)(socket, lpBuffers, dwBufferCount, lpNumberOfBytesRecvd, lpFlags, from, fromlen, lpOverlapped, lpCompletionRoutine);
		if (NetworkHookConfig.EnableNetworkHooks && NetworkHookConfig.NetworkVerbose && num >= 0 && from != 0)
		{
			ushort num2 = ReadPort((byte*)from);
			if (num2 == 7551 && !NetworkHookConfig.ShouldIgnorePort(num2) && lpBuffers != 0)
			{
				ProbeNetherNet((byte*)((Wsabuf*)lpBuffers)->Buf, (int)((Wsabuf*)lpBuffers)->Len, "wsarecv");
			}
		}
		return num;
	}

	private unsafe static ushort ReadPort(byte* sockaddr)
	{
		if (*sockaddr != AF_INET)
		{
			return 0;
		}
		return (ushort)((sockaddr[2] << 8) | sockaddr[3]);
	}

	private unsafe static bool IsLoopback(byte* sockaddr)
	{
		if (*sockaddr == AF_INET)
		{
			return sockaddr[4] == 127;
		}
		return false;
	}

	private static bool ShouldRedirectPort(ushort port)
	{
		if (port != 7551 && port != 19132 && port != 19133)
		{
			return port > 1024;
		}
		return true;
	}

	private static bool IsInterestingPort(ushort port)
	{
		if (port != 7551)
		{
			return port == NetworkHookConfig.NetworkListenPort;
		}
		return true;
	}

	private static void WriteAddress(Span<byte> sockaddr, string ip)
	{
		if (IPAddress.TryParse(ip, out IPAddress address))
		{
			byte[] addressBytes = address.GetAddressBytes();
			if (addressBytes.Length == 4)
			{
				addressBytes.CopyTo(sockaddr.Slice(4, 4));
			}
		}
	}

	private unsafe static string FormatAddress(byte* sockaddr)
	{
		if (*sockaddr != AF_INET)
		{
			return string.Empty;
		}
		return $"{sockaddr[4]}.{sockaddr[5]}.{sockaddr[6]}.{sockaddr[7]}";
	}

	private unsafe static void ProbeNetherNet(byte* buffer, int length, string direction)
	{
		if (length > 0)
		{
			string text = DecodeNetherNet(new ReadOnlySpan<byte>(buffer, length));
			if (text != null)
			{
				XUserBridge.Info("[nethernet] " + direction + " " + text);
			}
		}
	}

	private static string? DecodeNetherNet(ReadOnlySpan<byte> packet)
	{
		StringBuilder stringBuilder = new StringBuilder();
		int num = 0;
		ReadOnlySpan<byte> readOnlySpan = packet;
		for (int i = 0; i < readOnlySpan.Length; i++)
		{
			byte b = readOnlySpan[i];
			if (b >= 32 && b < 127)
			{
				stringBuilder.Append((char)b);
				num++;
			}
			else if (b == 0)
			{
				stringBuilder.Append(' ');
			}
		}
		string text = stringBuilder.ToString();
		if (num > 8 && (text.Contains("CONNECT", StringComparison.Ordinal) || text.Contains("CANDIDATE", StringComparison.Ordinal) || text.Contains("MCPE", StringComparison.Ordinal) || text.Contains(';')))
		{
			return "[NetherNet Plaintext] " + text.Trim();
		}
		if (packet.Length < 16)
		{
			return null;
		}
		byte[] key = SHA256.HashData(new byte[4] { 222, 173, 190, 239 });
		try
		{
			using Aes aes = Aes.Create();
			aes.Key = key;
			aes.Mode = CipherMode.ECB;
			aes.Padding = PaddingMode.None;
			int num2 = packet.Length / 16;
			byte[] array = new byte[num2 * 16];
			byte[] array2 = packet.Slice(0, num2 * 16).ToArray();
			aes.DecryptEcb(array2, array, PaddingMode.None);
			string text2 = Encoding.UTF8.GetString(array);
			if (text2.Contains("CONNECT", StringComparison.Ordinal) || text2.Contains("CANDIDATE", StringComparison.Ordinal) || text2.Contains("MCPE", StringComparison.Ordinal) || text2.Contains(';'))
			{
				return "[NetherNet Decrypted] " + Sanitize(text2);
			}
		}
		catch
		{
		}
		return null;
	}

	private static string Sanitize(string value)
	{
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		foreach (char c in value)
		{
			stringBuilder.Append((c >= ' ' && c < '\u007f') ? c : ' ');
		}
		return stringBuilder.ToString().Trim();
	}
}

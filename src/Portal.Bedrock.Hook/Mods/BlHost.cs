using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Portal.Bedrock.Hook.Mods;

internal static class BlHost
{
	private const string HostVersion = "0.1.0";

	private static string _gameDir = string.Empty;

	private static string _modsDir = string.Empty;

	private static string _cacheDir = string.Empty;

	public unsafe static nint Initialize(string gameDir, string modsDir)
	{
		_gameDir = gameDir;
		_modsDir = modsDir;
		_cacheDir = Path.Combine(modsDir, ".bl-cache");
		BlHostApiV1 structure = new BlHostApiV1
		{
			ApiVersion = 1u,
			Log = (nint)(delegate* unmanaged<uint, BlStringView, void>)(&HostLog),
			Register = (nint)(delegate* unmanaged<uint, BlStringView, nint, nint, int>)(&HostRegister),
			GetHostVersion = (nint)(delegate* unmanaged<byte*, nuint, nuint>)(&HostGetHostVersion),
			GetPath = (nint)(delegate* unmanaged<uint, byte*, nuint, nuint>)(&HostGetPath),
			ResolveSymbol = (nint)(delegate* unmanaged<BlStringView, nuint>)(&HostResolveSymbol),
			GetRuntimeInfo = (nint)(delegate* unmanaged<BlStringView, byte*, nuint, nuint>)(&HostGetRuntimeInfo),
			PathExists = (nint)(delegate* unmanaged<BlStringView, byte>)(&HostPathExists),
			CreateDir = (nint)(delegate* unmanaged<BlStringView, byte>)(&HostCreateDir),
			ReadTextFile = (nint)(delegate* unmanaged<BlStringView, byte*, nuint, nuint>)(&HostReadTextFile),
			WriteTextFile = (nint)(delegate* unmanaged<BlStringView, BlStringView, int>)(&HostWriteTextFile)
		};
		nint num = Marshal.AllocHGlobal(Marshal.SizeOf<BlHostApiV1>());
		Marshal.StructureToPtr(structure, num, fDeleteOld: false);
		return num;
	}

	private unsafe static string ReadString(BlStringView view)
	{
		if (view.Ptr != 0)
		{
			return Encoding.UTF8.GetString((byte*)view.Ptr, (int)view.Len);
		}
		return string.Empty;
	}

	private unsafe static nuint WriteUtf8(string value, byte* outBuffer, nuint outLen)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(value);
		nuint result = (nuint)(bytes.Length + 1);
		if (outBuffer == null)
		{
			return result;
		}
		long num = Math.Min(bytes.Length, Math.Max(0L, (long)outLen - 1L));
		Marshal.Copy(bytes, 0, (nint)outBuffer, (int)num);
		outBuffer[num] = 0;
		return result;
	}

	[UnmanagedCallersOnly(EntryPoint = "BlHostLog")]
	private static void HostLog(uint level, BlStringView message)
	{
		string text = ReadString(message);
		switch (level)
		{
		case 2u:
			XUserBridge.Warn("[BL Mod] " + text);
			break;
		case 3u:
			XUserBridge.Error("[BL Mod] " + text);
			break;
		default:
			XUserBridge.Info("[BL Mod] " + text);
			break;
		}
	}

	[UnmanagedCallersOnly(EntryPoint = "BlHostRegister")]
	private static int HostRegister(uint kind, BlStringView name, nint callback, nint userData)
	{
		return -4;
	}

	[UnmanagedCallersOnly(EntryPoint = "BlHostGetHostVersion")]
	private unsafe static nuint HostGetHostVersion(byte* outBuffer, nuint outLen)
	{
		return WriteUtf8("0.1.0", outBuffer, outLen);
	}

	[UnmanagedCallersOnly(EntryPoint = "BlHostGetPath")]
	private unsafe static nuint HostGetPath(uint kind, byte* outBuffer, nuint outLen)
	{
		return WriteUtf8(kind switch
		{
			1u => _gameDir, 
			2u => _modsDir, 
			3u => _cacheDir, 
			4u => string.Empty, 
			_ => string.Empty, 
		}, outBuffer, outLen);
	}

	[UnmanagedCallersOnly(EntryPoint = "BlHostResolveSymbol")]
	private static nuint HostResolveSymbol(BlStringView name)
	{
		return 0u;
	}

	[UnmanagedCallersOnly(EntryPoint = "BlHostGetRuntimeInfo")]
	private unsafe static nuint HostGetRuntimeInfo(BlStringView key, byte* outBuffer, nuint outLen)
	{
		return 0u;
	}

	[UnmanagedCallersOnly(EntryPoint = "BlHostPathExists")]
	private static byte HostPathExists(BlStringView path)
	{
		string text = ReadString(path);
		if (text.Length == 0)
		{
			return 0;
		}
		if (!File.Exists(text) && !Directory.Exists(text))
		{
			return 0;
		}
		return 1;
	}

	[UnmanagedCallersOnly(EntryPoint = "BlHostCreateDir")]
	private static byte HostCreateDir(BlStringView path)
	{
		try
		{
			Directory.CreateDirectory(ReadString(path));
			return 1;
		}
		catch
		{
			return 0;
		}
	}

	[UnmanagedCallersOnly(EntryPoint = "BlHostReadTextFile")]
	private unsafe static nuint HostReadTextFile(BlStringView path, byte* outBuffer, nuint outLen)
	{
		try
		{
			return WriteUtf8(File.ReadAllText(ReadString(path)), outBuffer, outLen);
		}
		catch
		{
			return 0u;
		}
	}

	[UnmanagedCallersOnly(EntryPoint = "BlHostWriteTextFile")]
	private static int HostWriteTextFile(BlStringView path, BlStringView content)
	{
		try
		{
			File.WriteAllText(ReadString(path), ReadString(content));
			return 0;
		}
		catch
		{
			return -1;
		}
	}
}

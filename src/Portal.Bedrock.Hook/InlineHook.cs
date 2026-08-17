using System;
using System.Runtime.InteropServices;

namespace Portal.Bedrock.Hook;

internal static class InlineHook
{
	internal static class NativeMethods
	{
		[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
		internal static extern nint VirtualAlloc(nint address, nuint size, uint allocationType, uint protect);

		[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool VirtualProtect(nint address, nuint size, uint newProtect, out uint oldProtect);

		[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool VirtualFree(nint address, nuint size, uint freeType);
	}

	private const uint MemCommit = 4096u;

	private const uint MemRelease = 32768u;

	private const uint PageExecuteReadWrite = 64u;

	private const int JumpSize = 14;

	private const int MinPatchLength = 14;

	private const int MaxDecodeBytes = 16;

	public unsafe static bool TryCreate(nint target, nint detour, out nint trampoline)
	{
		trampoline = 0;
		if (target == 0 || detour == 0)
		{
			XUserBridge.Warn($"InlineHook 参数无效 target=0x{target:X} detour=0x{detour:X}");
			return false;
		}
		byte* ptr = BuildTrampoline((byte*)target, out var _);
		if (ptr == null)
		{
			XUserBridge.Warn($"InlineHook 构建跳板失败 @0x{target:X} prologue={DumpBytes((byte*)target, 32)}");
			return false;
		}
		if (!PatchJump(target, detour))
		{
			XUserBridge.Warn($"InlineHook 写入跳转失败 @0x{target:X}");
			NativeMethods.VirtualFree((nint)ptr, 0u, 32768u);
			return false;
		}
		trampoline = (nint)ptr;
		return true;
	}

	private unsafe static string DumpBytes(byte* code, int count)
	{
		var sb = new System.Text.StringBuilder(3 * count);
		for (int i = 0; i < count; i++)
		{
			if (i > 0)
			{
				sb.Append(' ');
			}
			sb.Append(code[i].ToString("X2"));
		}
		return sb.ToString();
	}

	private unsafe static byte* BuildTrampoline(byte* target, out int patchLength)
	{
		patchLength = 0;
		while (patchLength < 14)
		{
			if (!X64Decoder.TryDecode(target + patchLength, 16 - patchLength, out var length, out var _))
			{
				return null;
			}
			if (length <= 0 || patchLength + length > 16)
			{
				return null;
			}
			patchLength += length;
		}
		byte* ptr = (byte*)NativeMethods.VirtualAlloc(IntPtr.Zero, (nuint)(patchLength + 14), 4096u, 64u);
		if (ptr == null)
		{
			return null;
		}
		for (int i = 0; i < patchLength; i++)
		{
			ptr[i] = target[i];
		}
		RelocateRipRelative(ptr, target, patchLength);
		PatchJump((nint)(ptr + patchLength), (nint)(target + patchLength));
		return ptr;
	}

	private unsafe static void RelocateRipRelative(byte* trunk, byte* target, int length)
	{
		int length2;
		int ripDispOffset;
		for (int i = 0; i < length && X64Decoder.TryDecode(target + i, length - i, out length2, out ripDispOffset); i += length2)
		{
			if (ripDispOffset >= 0)
			{
				long num = *(int*)(target + i + ripDispOffset) + ((long)target - (long)trunk);
				*(int*)(trunk + i + ripDispOffset) = (int)num;
			}
		}
	}

	private unsafe static bool PatchJump(nint location, nint destination)
	{
		if (!NativeMethods.VirtualProtect(location, 14u, 64u, out var oldProtect))
		{
			return false;
		}
		*(sbyte*)location = -1;
		*(sbyte*)(location + 1) = 37;
		*(int*)(location + 2) = 0;
		*(long*)(location + 6) = destination;
		NativeMethods.VirtualProtect(location, 14u, oldProtect, out var _);
		return true;
	}
}

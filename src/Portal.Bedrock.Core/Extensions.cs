using System;
using System.Runtime.InteropServices;

namespace Portal.Bedrock.Core;

internal static class Extensions
{
	public unsafe static T GetstructFromBytes<T>(ReadOnlySpan<byte> bytes) where T : struct
	{
		int num = Marshal.SizeOf<T>();
		if (bytes.Length < num)
		{
			throw new ArgumentException("Bytes out of length");
		}
		fixed (byte* ptr = bytes)
		{
			return Marshal.PtrToStructure<T>((nint)ptr);
		}
	}

	public unsafe static T[] GetstructArraysFromBytes<T>(ReadOnlySpan<byte> bytes, long counts) where T : struct
	{
		int num = Marshal.SizeOf(typeof(T));
		if (bytes.Length < num * counts)
		{
			throw new ArgumentException("Bytes out of length");
		}
		fixed (byte* ptr = bytes)
		{
			T[] array = new T[counts];
			for (int i = 0; i < counts; i++)
			{
				array[i] = Marshal.PtrToStructure<T>((nint)(ptr + num * i));
			}
			return array;
		}
	}

	public static ulong GetPageOffset(ulong sourceUlong)
	{
		return sourceUlong / 4096;
	}

	public static ulong PageToOffset(ulong sourceUlong)
	{
		return sourceUlong * 4096;
	}

	public static ulong BytesToPages(ulong bytes)
	{
		return (bytes + 4096 - 1) / 4096;
	}

	public static ulong ComputeHashBlockIndexForDataBlock(MsiXVDKind imageType, ulong hashTreeDepth, ulong totalHashedPages, ulong dataBlockIndex, uint currentHashLevel, out ulong entryIndexInHashBlock, bool isResilient = false, bool isUnknown = false)
	{
		ulong result = 65535uL;
		entryIndexInHashBlock = 0uL;
		if (imageType > MsiXVDKind.Dynamic || currentHashLevel > 3)
		{
			return result;
		}
		if (currentHashLevel == 0)
		{
			entryIndexInHashBlock = dataBlockIndex % 170;
		}
		else
		{
			entryIndexInHashBlock = dataBlockIndex / ComputeLevelMultiplier(currentHashLevel) % 170;
		}
		if (currentHashLevel == 3)
		{
			return 0uL;
		}
		result = dataBlockIndex / ComputeLevelMultiplier(currentHashLevel + 1);
		hashTreeDepth -= currentHashLevel + 1;
		if (currentHashLevel == 0 && hashTreeDepth != 0)
		{
			result += (totalHashedPages + ComputeLevelMultiplier(2uL) - 1) / ComputeLevelMultiplier(2uL);
			hashTreeDepth--;
		}
		if ((currentHashLevel == 0 || currentHashLevel == 1) && hashTreeDepth != 0)
		{
			result += (totalHashedPages + ComputeLevelMultiplier(3uL) - 1) / ComputeLevelMultiplier(3uL);
			hashTreeDepth--;
		}
		if (hashTreeDepth != 0)
		{
			result += (totalHashedPages + ComputeLevelMultiplier(4uL) - 1) / ComputeLevelMultiplier(4uL);
		}
		if (isResilient)
		{
			result *= 2;
		}
		if (isUnknown)
		{
			result++;
		}
		return result;
	}

	private static ulong ComputeLevelMultiplier(ulong levelCount)
	{
		return (ulong)Math.Pow(170.0, levelCount);
	}
}

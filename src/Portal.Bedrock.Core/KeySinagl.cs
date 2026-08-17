using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Portal.Bedrock.Core;

[StructLayout(LayoutKind.Sequential, Size = 176)]
public struct KeySinagl
{
	public Vector128<byte> Keys;

	public readonly ReadOnlySpan<Vector128<byte>> RKeys => MemoryMarshal.CreateReadOnlySpan(in Unsafe.AsRef(in Keys), 11);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Vector128<byte> KeyExpansion(Vector128<byte> s, Vector128<byte> t)
	{
		t = Sse2.Shuffle(t.AsUInt32(), byte.MaxValue).AsByte();
		s = Sse2.Xor(s, Sse2.ShiftLeftLogical128BitLane(s, 4));
		s = Sse2.Xor(s, Sse2.ShiftLeftLogical128BitLane(s, 8));
		return Sse2.Xor(s, t);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public readonly Vector128<byte> DecryptBlockUnrolled(Vector128<byte> input)
	{
		ReadOnlySpan<Vector128<byte>> rKeys = RKeys;
		return Aes.DecryptLast(Aes.Decrypt(Aes.Decrypt(Aes.Decrypt(Aes.Decrypt(Aes.Decrypt(Aes.Decrypt(Aes.Decrypt(Aes.Decrypt(Aes.Decrypt(Sse2.Xor(input, rKeys[10]), rKeys[9]), rKeys[8]), rKeys[7]), rKeys[6]), rKeys[5]), rKeys[4]), rKeys[3]), rKeys[2]), rKeys[1]), rKeys[0]);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public readonly Vector128<byte> EncryptUnrolled(Vector128<byte> input)
	{
		ReadOnlySpan<Vector128<byte>> rKeys = RKeys;
		return Aes.EncryptLast(Aes.Encrypt(Aes.Encrypt(Aes.Encrypt(Aes.Encrypt(Aes.Encrypt(Aes.Encrypt(Aes.Encrypt(Aes.Encrypt(Aes.Encrypt(Sse2.Xor(input, rKeys[0]), rKeys[1]), rKeys[2]), rKeys[3]), rKeys[4]), rKeys[5]), rKeys[6]), rKeys[7]), rKeys[8]), rKeys[9]), rKeys[10]);
	}

	public void Init(ReadOnlySpan<byte> keyBytes, bool isDecryption)
	{
		if (keyBytes.Length < 16)
		{
			throw new ArgumentException("Key Length is not enough", "keyBytes");
		}
		Span<Vector128<byte>> span = MemoryMarshal.CreateSpan(ref Keys, 11);
		ReadOnlySpan<byte> readOnlySpan = new byte[10] { 1, 2, 4, 8, 16, 32, 64, 128, 27, 54 };
		span[0] = Unsafe.ReadUnaligned<Vector128<byte>>(in MemoryMarshal.GetReference(keyBytes));
		for (int i = 0; i < 10; i++)
		{
			span[i + 1] = KeyExpansion(span[i], Aes.KeygenAssist(span[i], readOnlySpan[i]));
		}
		if (isDecryption)
		{
			for (int j = 1; j < 10; j++)
			{
				span[j] = Aes.InverseMixColumns(span[j]);
			}
		}
	}
}

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;

namespace Portal.Bedrock.Core;

public class MsiXVDDecoder : IDisposable
{
	public KeySinagl d;

	public KeySinagl t;

	private readonly bool _useHardware;

	private readonly System.Security.Cryptography.Aes? _swAesD;

	private readonly System.Security.Cryptography.Aes? _swAesT;

	public MsiXVDDecoder(CikKey key)
		: this(key, useHardware: true)
	{
	}

	public MsiXVDDecoder(CikKey key, bool useHardware)
	{
		_useHardware = useHardware && Sse2.IsSupported && System.Runtime.Intrinsics.X86.Aes.IsSupported;
		if (_useHardware)
		{
			d.Init(key.DKey, isDecryption: true);
			t.Init(key.TKey, isDecryption: false);
			_swAesD = null;
			_swAesT = null;
			return;
		}
		_swAesD = System.Security.Cryptography.Aes.Create();
		_swAesD.KeySize = 128;
		_swAesD.Key = key.DKey;
		_swAesD.Mode = CipherMode.ECB;
		_swAesD.Padding = PaddingMode.None;
		_swAesT = System.Security.Cryptography.Aes.Create();
		_swAesT.KeySize = 128;
		_swAesT.Key = key.TKey;
		_swAesT.Mode = CipherMode.ECB;
		_swAesT.Padding = PaddingMode.None;
	}

	public int Decrypt(ReadOnlySpan<byte> input, Span<byte> output, ReadOnlySpan<byte> tweakIv)
	{
		return _useHardware ? DecryptHardware(input, output, tweakIv) : DecryptSoftware(input, output, tweakIv);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Vector128<byte> Gf128Mul(Vector128<byte> iv, Vector128<byte> mask)
	{
		Vector128<byte> left = Sse2.Add(iv.AsUInt64(), iv.AsUInt64()).AsByte();
		Vector128<byte> vector = Sse2.Shuffle(iv.AsInt32(), 19).AsByte();
		vector = Sse2.ShiftRightArithmetic(vector.AsInt32(), 31).AsByte();
		vector = Sse2.And(mask, vector);
		return Sse2.Xor(left, vector);
	}

	private int DecryptHardware(ReadOnlySpan<byte> input, Span<byte> output, ReadOnlySpan<byte> tweakIv)
	{
		if (tweakIv.Length < 16)
		{
			return 0;
		}
		Vector128<byte> input2 = Unsafe.ReadUnaligned<Vector128<byte>>(in MemoryMarshal.GetReference(tweakIv));
		int num = Math.Min(input.Length, output.Length);
		if (num == 0)
		{
			return 0;
		}
		int num2 = num >> 4;
		int num3 = num & 0xF;
		if (num3 != 0)
		{
			num2--;
		}
		if (num2 <= 0 && num3 == 0)
		{
			return 0;
		}
		ref Vector128<byte> reference = ref Unsafe.As<byte, Vector128<byte>>(ref MemoryMarshal.GetReference(input));
		ref Vector128<byte> reference2 = ref Unsafe.As<byte, Vector128<byte>>(ref MemoryMarshal.GetReference(output));
		Vector128<byte> mask = Vector128.Create(135L, 1L).AsByte();
		Vector128<byte> vector = t.EncryptUnrolled(input2);
		while (num2 > 7)
		{
			Vector128<byte> vector2 = Gf128Mul(vector, mask);
			Vector128<byte> vector3 = Gf128Mul(vector2, mask);
			Vector128<byte> vector4 = Gf128Mul(vector3, mask);
			Vector128<byte> vector5 = Gf128Mul(vector4, mask);
			Vector128<byte> vector6 = Gf128Mul(vector5, mask);
			Vector128<byte> vector7 = Gf128Mul(vector6, mask);
			Vector128<byte> vector8 = Gf128Mul(vector7, mask);
			Vector128<byte> iv = Gf128Mul(vector8, mask);
			Vector128<byte> @in = Sse2.Xor(vector, Unsafe.Add(ref reference, 0));
			Vector128<byte> in2 = Sse2.Xor(vector2, Unsafe.Add(ref reference, 1));
			Vector128<byte> in3 = Sse2.Xor(vector3, Unsafe.Add(ref reference, 2));
			Vector128<byte> in4 = Sse2.Xor(vector4, Unsafe.Add(ref reference, 3));
			Vector128<byte> in5 = Sse2.Xor(vector5, Unsafe.Add(ref reference, 4));
			Vector128<byte> in6 = Sse2.Xor(vector6, Unsafe.Add(ref reference, 5));
			Vector128<byte> in7 = Sse2.Xor(vector7, Unsafe.Add(ref reference, 6));
			Vector128<byte> in8 = Sse2.Xor(vector8, Unsafe.Add(ref reference, 7));
			DecryptBlocks8(@in, in2, in3, in4, in5, in6, in7, in8, out var @out, out var out2, out var out3, out var out4, out var out5, out var out6, out var out7, out var out8);
			Unsafe.Add(ref reference2, 0) = Sse2.Xor(vector, @out);
			Unsafe.Add(ref reference2, 1) = Sse2.Xor(vector2, out2);
			Unsafe.Add(ref reference2, 2) = Sse2.Xor(vector3, out3);
			Unsafe.Add(ref reference2, 3) = Sse2.Xor(vector4, out4);
			Unsafe.Add(ref reference2, 4) = Sse2.Xor(vector5, out5);
			Unsafe.Add(ref reference2, 5) = Sse2.Xor(vector6, out6);
			Unsafe.Add(ref reference2, 6) = Sse2.Xor(vector7, out7);
			Unsafe.Add(ref reference2, 7) = Sse2.Xor(vector8, out8);
			vector = Gf128Mul(iv, mask);
			reference = ref Unsafe.Add(ref reference, 8);
			reference2 = ref Unsafe.Add(ref reference2, 8);
			num2 -= 8;
		}
		while (num2 > 0)
		{
			Vector128<byte> input3 = Sse2.Xor(reference, vector);
			input3 = d.DecryptBlockUnrolled(input3);
			reference2 = Sse2.Xor(input3, vector);
			vector = Gf128Mul(vector, mask);
			reference = ref Unsafe.Add(ref reference, 1);
			reference2 = ref Unsafe.Add(ref reference2, 1);
			num2--;
		}
		if (num3 != 0)
		{
			Vector128<byte> right = Gf128Mul(vector, mask);
			Vector128<byte> input4 = Sse2.Xor(reference, right);
			input4 = d.DecryptBlockUnrolled(input4);
			reference2 = Sse2.Xor(input4, right);
			Span<byte> span = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref reference2, 1));
			Span<byte> span2 = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref Unsafe.Add(ref reference, 1), 1));
			Span<byte> span3 = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref Unsafe.Add(ref reference2, 1), 1));
			Span<byte> span4 = stackalloc byte[16];
			for (int i = 0; i < num3; i++)
			{
				span3[i] = span[i];
				span4[i] = span2[i];
			}
			for (int j = num3; j < 16; j++)
			{
				span4[j] = span[j];
			}
			input4 = Unsafe.ReadUnaligned<Vector128<byte>>(in span4[0]);
			input4 = Sse2.Xor(input4, vector);
			input4 = d.DecryptBlockUnrolled(input4);
			reference2 = Sse2.Xor(input4, vector);
		}
		return num;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void DecryptBlocks8(Vector128<byte> in0, Vector128<byte> in1, Vector128<byte> in2, Vector128<byte> in3, Vector128<byte> in4, Vector128<byte> in5, Vector128<byte> in6, Vector128<byte> in7, out Vector128<byte> out0, out Vector128<byte> out1, out Vector128<byte> out2, out Vector128<byte> out3, out Vector128<byte> out4, out Vector128<byte> out5, out Vector128<byte> out6, out Vector128<byte> out7)
	{
		ReadOnlySpan<Vector128<byte>> rKeys = d.RKeys;
		Vector128<byte> right = rKeys[10];
		Vector128<byte> value = Sse2.Xor(in0, right);
		Vector128<byte> value2 = Sse2.Xor(in1, right);
		Vector128<byte> value3 = Sse2.Xor(in2, right);
		Vector128<byte> value4 = Sse2.Xor(in3, right);
		Vector128<byte> value5 = Sse2.Xor(in4, right);
		Vector128<byte> value6 = Sse2.Xor(in5, right);
		Vector128<byte> value7 = Sse2.Xor(in6, right);
		Vector128<byte> value8 = Sse2.Xor(in7, right);
		right = rKeys[9];
		value = System.Runtime.Intrinsics.X86.Aes.Decrypt(value, right);
		value2 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value2, right);
		value3 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value3, right);
		value4 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value4, right);
		value5 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value5, right);
		value6 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value6, right);
		value7 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value7, right);
		value8 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value8, right);
		right = rKeys[8];
		value = System.Runtime.Intrinsics.X86.Aes.Decrypt(value, right);
		value2 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value2, right);
		value3 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value3, right);
		value4 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value4, right);
		value5 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value5, right);
		value6 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value6, right);
		value7 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value7, right);
		value8 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value8, right);
		right = rKeys[7];
		value = System.Runtime.Intrinsics.X86.Aes.Decrypt(value, right);
		value2 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value2, right);
		value3 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value3, right);
		value4 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value4, right);
		value5 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value5, right);
		value6 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value6, right);
		value7 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value7, right);
		value8 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value8, right);
		right = rKeys[6];
		value = System.Runtime.Intrinsics.X86.Aes.Decrypt(value, right);
		value2 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value2, right);
		value3 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value3, right);
		value4 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value4, right);
		value5 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value5, right);
		value6 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value6, right);
		value7 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value7, right);
		value8 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value8, right);
		right = rKeys[5];
		value = System.Runtime.Intrinsics.X86.Aes.Decrypt(value, right);
		value2 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value2, right);
		value3 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value3, right);
		value4 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value4, right);
		value5 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value5, right);
		value6 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value6, right);
		value7 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value7, right);
		value8 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value8, right);
		right = rKeys[4];
		value = System.Runtime.Intrinsics.X86.Aes.Decrypt(value, right);
		value2 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value2, right);
		value3 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value3, right);
		value4 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value4, right);
		value5 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value5, right);
		value6 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value6, right);
		value7 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value7, right);
		value8 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value8, right);
		right = rKeys[3];
		value = System.Runtime.Intrinsics.X86.Aes.Decrypt(value, right);
		value2 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value2, right);
		value3 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value3, right);
		value4 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value4, right);
		value5 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value5, right);
		value6 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value6, right);
		value7 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value7, right);
		value8 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value8, right);
		right = rKeys[2];
		value = System.Runtime.Intrinsics.X86.Aes.Decrypt(value, right);
		value2 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value2, right);
		value3 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value3, right);
		value4 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value4, right);
		value5 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value5, right);
		value6 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value6, right);
		value7 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value7, right);
		value8 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value8, right);
		right = rKeys[1];
		value = System.Runtime.Intrinsics.X86.Aes.Decrypt(value, right);
		value2 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value2, right);
		value3 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value3, right);
		value4 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value4, right);
		value5 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value5, right);
		value6 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value6, right);
		value7 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value7, right);
		value8 = System.Runtime.Intrinsics.X86.Aes.Decrypt(value8, right);
		right = rKeys[0];
		out0 = System.Runtime.Intrinsics.X86.Aes.DecryptLast(value, right);
		out1 = System.Runtime.Intrinsics.X86.Aes.DecryptLast(value2, right);
		out2 = System.Runtime.Intrinsics.X86.Aes.DecryptLast(value3, right);
		out3 = System.Runtime.Intrinsics.X86.Aes.DecryptLast(value4, right);
		out4 = System.Runtime.Intrinsics.X86.Aes.DecryptLast(value5, right);
		out5 = System.Runtime.Intrinsics.X86.Aes.DecryptLast(value6, right);
		out6 = System.Runtime.Intrinsics.X86.Aes.DecryptLast(value7, right);
		out7 = System.Runtime.Intrinsics.X86.Aes.DecryptLast(value8, right);
	}

	private int DecryptSoftware(ReadOnlySpan<byte> input, Span<byte> output, ReadOnlySpan<byte> tweakIv)
	{
		if (tweakIv.Length < 16)
		{
			return 0;
		}
		int num = Math.Min(input.Length, output.Length);
		if (num == 0)
		{
			return 0;
		}
		int num2 = num >> 4;
		int num3 = num & 0xF;
		if (num3 != 0)
		{
			num2--;
		}
		if (num2 <= 0 && num3 == 0)
		{
			return 0;
		}
		byte[] array = new byte[16];
		_swAesT.EncryptEcb(tweakIv.Slice(0, 16), array, PaddingMode.None);
		Span<byte> span = stackalloc byte[16];
		Span<byte> span2 = stackalloc byte[16];
		int num4 = 0;
		while (num2 > 0)
		{
			XorBytes(input.Slice(num4, 16), array, span);
			_swAesD.DecryptEcb(span, span2, PaddingMode.None);
			XorBytes(span2, array, output.Slice(num4, 16));
			Gf128MulSoftware(array);
			num4 += 16;
			num2--;
		}
		if (num3 != 0)
		{
			byte[] subArray = array[..];
			Gf128MulSoftware(subArray);
			XorBytes(input.Slice(num4, 16), subArray, span);
			_swAesD.DecryptEcb(span, span2, PaddingMode.None);
			XorBytes(span2, subArray, output.Slice(num4, 16));
			Span<byte> span3 = output.Slice(num4, 16);
			ReadOnlySpan<byte> readOnlySpan = input.Slice(num4 + 16, num3);
			Span<byte> span4 = output.Slice(num4 + 16, num3);
			Span<byte> span5 = stackalloc byte[16];
			for (int i = 0; i < num3; i++)
			{
				span4[i] = span3[i];
				span5[i] = readOnlySpan[i];
			}
			for (int j = num3; j < 16; j++)
			{
				span5[j] = span3[j];
			}
			XorBytes(span5, array, span);
			_swAesD.DecryptEcb(span, span2, PaddingMode.None);
			XorBytes(span2, array, output.Slice(num4, 16));
		}
		return num;
	}

	private static void XorBytes(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, Span<byte> result)
	{
		for (int i = 0; i < 16; i++)
		{
			result[i] = (byte)(a[i] ^ b[i]);
		}
	}

	private static void Gf128MulSoftware(Span<byte> iv)
	{
		int num = (iv[15] >> 7) & 1;
		int num2 = (iv[7] >> 7) & 1;
		for (int num3 = 7; num3 > 0; num3--)
		{
			iv[num3] = (byte)((iv[num3] << 1) | (iv[num3 - 1] >> 7));
		}
		iv[0] <<= 1;
		for (int num4 = 15; num4 > 8; num4--)
		{
			iv[num4] = (byte)((iv[num4] << 1) | (iv[num4 - 1] >> 7));
		}
		iv[8] <<= 1;
		if (num2 != 0)
		{
			iv[8] ^= 1;
		}
		if (num != 0)
		{
			iv[0] ^= 135;
		}
	}

	public void Dispose()
	{
		_swAesD?.Dispose();
		_swAesT?.Dispose();
		GC.SuppressFinalize(this);
	}
}

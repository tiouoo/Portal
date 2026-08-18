using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Portal.Bedrock.Hook;

internal sealed class XUserSigningKey : IDisposable
{
	private const uint BcryptEcdsaPrivateP256Magic = 844317509u;

	private const ulong WindowsToUnixEpochSeconds = 11644473600uL;

	private const ulong FileTimeTicksPerSecond = 10000000uL;

	private const uint SignaturePolicyVersion = 1u;

	private const int SignatureHeaderSize = 76;

	private readonly ECDsa _ecdsa;

	private XUserSigningKey(ECDsa ecdsa)
	{
		_ecdsa = ecdsa;
	}

	public static XUserSigningKey? ImportPrivateBlob(byte[] blob)
	{
		if (blob.Length != 104 || BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(0, 4)) != BcryptEcdsaPrivateP256Magic || BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(4, 4)) != 32)
		{
			return null;
		}
		byte[] array = blob.AsSpan(8, 32).ToArray();
		byte[] array2 = blob.AsSpan(40, 32).ToArray();
		byte[] array3 = blob.AsSpan(72, 32).ToArray();
		try
		{
			ECDsa eCDsa = ECDsa.Create();
			try
			{
				eCDsa.ImportParameters(new ECParameters
				{
					Curve = ECCurve.NamedCurves.nistP256,
					Q = new ECPoint
					{
						X = array,
						Y = array2
					},
					D = array3
				});
			}
			catch
			{
				eCDsa.Dispose();
				throw;
			}
			return new XUserSigningKey(eCDsa);
		}
		catch
		{
			return null;
		}
		finally
		{
			CryptographicOperations.ZeroMemory(array);
			CryptographicOperations.ZeroMemory(array2);
			CryptographicOperations.ZeroMemory(array3);
		}
	}

	public string SignRequest(string method, string requestTarget, string authorization, IReadOnlyList<string> policyHeaderValues, ReadOnlySpan<byte> body)
	{
		if (method.Length == 0 || requestTarget.Length == 0 || !IsAscii(method) || !IsAscii(requestTarget))
		{
			throw new ArgumentException("invalid signing input");
		}
		ulong value = CurrentFileTime();
		byte[] bytes = Encoding.ASCII.GetBytes(method.ToUpperInvariant());
		using IncrementalHash incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		Span<byte> span = stackalloc byte[4];
		Span<byte> span2 = stackalloc byte[8];
		Span<byte> span3 = stackalloc byte[1];
		BinaryPrimitives.WriteUInt32BigEndian(span, SignaturePolicyVersion);
		BinaryPrimitives.WriteUInt64BigEndian(span2, value);
		incrementalHash.AppendData(span);
		incrementalHash.AppendData(span3);
		incrementalHash.AppendData(span2);
		incrementalHash.AppendData(span3);
		incrementalHash.AppendData(bytes);
		incrementalHash.AppendData(span3);
		incrementalHash.AppendData(Encoding.ASCII.GetBytes(requestTarget));
		incrementalHash.AppendData(span3);
		incrementalHash.AppendData(Encoding.ASCII.GetBytes(authorization));
		incrementalHash.AppendData(span3);
		foreach (string policyHeaderValue in policyHeaderValues)
		{
			incrementalHash.AppendData(Encoding.ASCII.GetBytes(policyHeaderValue));
			incrementalHash.AppendData(span3);
		}
		incrementalHash.AppendData(body);
		incrementalHash.AppendData(span3);
		byte[] hashAndReset = incrementalHash.GetHashAndReset();
		try
		{
			byte[] array = _ecdsa.SignHash(hashAndReset, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
			if (array.Length != 64)
			{
				throw new CryptographicException("invalid signature size");
			}
			byte[] array2 = new byte[SignatureHeaderSize];
			try
			{
				BinaryPrimitives.WriteUInt32BigEndian(array2.AsSpan(0, 4), 1u);
				BinaryPrimitives.WriteUInt64BigEndian(array2.AsSpan(4, 8), value);
				array.CopyTo(array2, 12);
				return Convert.ToBase64String(array2);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(array2);
			}
		}
		finally
		{
			CryptographicOperations.ZeroMemory(hashAndReset);
		}
	}

	private static ulong CurrentFileTime()
	{
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		return (ulong)((WindowsToUnixEpochSeconds + (ulong)Math.Max(0L, utcNow.ToUnixTimeSeconds())) * FileTimeTicksPerSecond) + (ulong)utcNow.Microsecond / 10uL;
	}

	private static bool IsAscii(string value)
	{
		for (int i = 0; i < value.Length; i++)
		{
			if (value[i] > '\u007f')
			{
				return false;
			}
		}
		return true;
	}

	public void Dispose()
	{
		_ecdsa.Dispose();
	}
}

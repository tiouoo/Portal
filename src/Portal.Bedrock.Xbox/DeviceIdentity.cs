using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Portal.Bedrock.Xbox;

public sealed class DeviceIdentity : IDisposable
{
	private const uint BcryptEcdsaPrivateP256Magic = 844317509u;

	public string Id { get; }

	public ECDsa SigningKey { get; }

	private DeviceIdentity(string id, ECDsa signingKey)
	{
		Id = id;
		SigningKey = signingKey;
	}

	public static DeviceIdentity Create(string? deviceId = null, byte[]? privateBlob = null)
	{
		return new DeviceIdentity(IsBracedGuid(deviceId) ? deviceId! : $"{{{Guid.NewGuid()}}}", (privateBlob != null) ? ImportPrivateBlob(privateBlob) : ECDsa.Create(ECCurve.NamedCurves.nistP256));
	}

	private static ECDsa ImportPrivateBlob(byte[] blob)
	{
		if (blob.Length != 104 || BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(0, 4)) != 844317509 || BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(4, 4)) != 32)
		{
			throw new InvalidDataException("保存的 Xbox P-256 私钥 blob 无效。");
		}
		byte[] array = blob.AsSpan(8, 32).ToArray();
		byte[] array2 = blob.AsSpan(40, 32).ToArray();
		byte[] array3 = blob.AsSpan(72, 32).ToArray();
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
			return eCDsa;
		}
		catch
		{
			eCDsa.Dispose();
			throw;
		}
		finally
		{
			CryptographicOperations.ZeroMemory(array);
			CryptographicOperations.ZeroMemory(array2);
			CryptographicOperations.ZeroMemory(array3);
		}
	}

	public Dictionary<string, string> CreateProofKey()
	{
		ECParameters eCParameters = SigningKey.ExportParameters(includePrivateParameters: false);
		byte[] x = eCParameters.Q.X;
		byte[] y = eCParameters.Q.Y;
		if (x != null && x.Length == 32 && y != null && y.Length == 32)
		{
			return new Dictionary<string, string>
			{
				["alg"] = "ES256",
				["crv"] = "P-256",
				["kty"] = "EC",
				["use"] = "sig",
				["x"] = Convert.ToBase64String(x),
				["y"] = Convert.ToBase64String(y)
			};
		}
		throw new CryptographicException("P-256 公钥坐标无效。");
	}

	public byte[] ExportBcryptPrivateBlob()
	{
		ECParameters eCParameters = SigningKey.ExportParameters(includePrivateParameters: true);
		byte[] x = eCParameters.Q.X;
		byte[] y = eCParameters.Q.Y;
		byte[] d = eCParameters.D;
		if (x != null && x.Length == 32 && y != null && y.Length == 32 && d != null && d.Length == 32)
		{
			byte[] array = new byte[104];
			BinaryPrimitives.WriteUInt32LittleEndian(array.AsSpan(0, 4), 844317509u);
			BinaryPrimitives.WriteUInt32LittleEndian(array.AsSpan(4, 4), 32u);
			x.CopyTo(array, 8);
			y.CopyTo(array, 40);
			d.CopyTo(array, 72);
			return array;
		}
		throw new CryptographicException("P-256 私钥参数不完整。");
	}

	public byte[] SignP1363(ReadOnlySpan<byte> data)
	{
		return SigningKey.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
	}

	private static bool IsBracedGuid(string? value)
	{
		if (value == null || value.Length < 2 || value[0] != '{')
		{
			return false;
		}
		if (value[value.Length - 1] != '}')
		{
			return false;
		}
		Guid result;
		return Guid.TryParse(value.Substring(1, value.Length - 2), out result);
	}

	public void Dispose()
	{
		SigningKey.Dispose();
	}
}

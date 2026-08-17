using System;

namespace Portal.Bedrock.Core;

public class CikKey
{
	public const byte MaxSize = 48;

	public readonly Guid Guid;

	public byte[] TKey;

	public byte[] DKey;

	public CikKey(ReadOnlySpan<byte> cik)
	{
		Guid = new Guid(cik.Slice(0, 16));
		TKey = cik.Slice(16, 16).ToArray();
		DKey = cik.Slice(32).ToArray();
	}

	public CikKey(string hexString)
	{
		byte[] array = Convert.FromHexString(hexString);
		Guid = new Guid(array[..16]);
		TKey = array[16..32];
		DKey = array[32..];
	}
}

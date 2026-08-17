using System.Runtime.InteropServices;

namespace Portal.Bedrock.Core;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct XvcEncryptionKeyId
{
	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
	public byte[] KeyId;
}

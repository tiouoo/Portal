using System.Runtime.InteropServices;

namespace Portal.Bedrock.Core;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct XvcUpdateSegment
{
	public uint PageNum;

	public ulong Hash;
}

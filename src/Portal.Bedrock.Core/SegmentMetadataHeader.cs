using System.Runtime.InteropServices;

namespace Portal.Bedrock.Core;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
public struct SegmentMetadataHeader
{
	public uint Magic;

	public uint Version0;

	public uint Version1;

	public uint HeaderLength;

	public uint SegmentCount;

	public uint FilePathsLength;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
	public byte[] PDUID;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 60)]
	public byte[] Unknown;
}

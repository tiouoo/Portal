using System.Runtime.InteropServices;

namespace Portal.Bedrock.Core;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1, Size = 16)]
public struct SegmentsAbout
{
	public SegmentMetadataFlags Flags;

	public ushort PathLength;

	public uint PathOffset;

	public ulong FileSize;
}

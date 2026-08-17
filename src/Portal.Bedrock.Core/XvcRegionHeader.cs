using System.Runtime.InteropServices;

namespace Portal.Bedrock.Core;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
public struct XvcRegionHeader
{
	public XvcRegionId Id;

	public ushort KeyId;

	public ushort Padding6;

	public XvcRegionFlags Flags;

	public uint FirstSegmentIndex;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
	public string Description;

	public ulong Offset;

	public ulong Length;

	public ulong Hash;

	public ulong Unknown68;

	public ulong Unknown70;

	public ulong Unknown78;
}

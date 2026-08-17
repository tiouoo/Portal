using System.Runtime.InteropServices;

namespace Portal.Bedrock.Core;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
public struct XvcRegionSpecifier
{
	public XvcRegionId RegionId;

	public uint Padding4;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
	public string Key;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
	public string Value;
}

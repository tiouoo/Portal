using System.Runtime.InteropServices;

namespace Portal.Bedrock.Core;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
public struct UserDataPackageFileEntry
{
	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
	public string FilePath;

	public uint Size;

	public uint Offset;
}

using System.Runtime.InteropServices;

namespace Portal.Bedrock.Core;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MsiXVDHeader
{
	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
	public byte[] Signature;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
	public char[] Magic;

	public MsiXVDVolumeAttributes Volumes;

	public uint FormatVersion;

	public long FileTimeCreated;

	public ulong DriveSize;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
	public byte[] VdUid;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
	public byte[] UdUid;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
	public byte[] TopHashBlockHash;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
	public byte[] OriginalXvcDataHash;

	public MsiXVDKind Kind;

	public MsiXVDContentCategory Category;

	public uint EmbeddedXvdLength;

	public uint UserDataLength;

	public uint XvcDataLength;

	public uint DynamicHeaderLength;

	public uint BlockSize;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
	public ExtEntry[] ExtEntries;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
	public ushort[] Capabilities;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
	public byte[] PeCatalogHash;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
	public byte[] EmbeddedXvdPdUid;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
	public byte[] Reserved13C;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
	public byte[] KeyMaterial;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
	public byte[] UserDataHash;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
	public char[] SandboxId;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
	public byte[] ProductId;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
	public byte[] PdUid;

	public ushort PackageVersion1;

	public ushort PackageVersion2;

	public ushort PackageVersion3;

	public ushort PackageVersion4;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
	public ushort[] PeCatalogCaps;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
	public byte[] PeCatalogs;

	public uint WriteableExpirationDate;

	public uint WriteablePolicyFlags;

	public uint PersistentLocalStorageSize;

	public byte MutableDataPageCount;

	public byte Unknown271;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
	public byte[] Unknown272;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
	public byte[] Reserved282;

	public long SequenceNumber;

	public ushort Unknown1;

	public ushort Unknown2;

	public ushort Unknown3;

	public ushort Unknown4;

	public MsiXVDOdkIndex OdkKeyslotId;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2900)]
	public byte[] Reserved2A0;

	public ulong ResilientDataOffset;

	public uint ResilientDataLength;

	public ulong MutableDataLength => Extensions.PageToOffset(MutableDataPageCount);

	public ulong UserDataPageCount => Extensions.BytesToPages(UserDataLength);

	public ulong XvcInfoPageCount => Extensions.BytesToPages(XvcDataLength);

	public ulong EmbeddedXvdPageCount => Extensions.BytesToPages(EmbeddedXvdLength);

	public ulong DynamicHeaderPageCount => Extensions.BytesToPages(DynamicHeaderLength);

	public ulong DrivePageCount => Extensions.BytesToPages(DriveSize);

	public ulong NumberOfHashedPages => DrivePageCount + UserDataPageCount + XvcInfoPageCount + DynamicHeaderPageCount;

	public ulong NumberOfMetadataPages => UserDataPageCount + XvcInfoPageCount + DynamicHeaderPageCount;
}

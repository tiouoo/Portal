using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Portal.Bedrock.Core;

public class MsiXVDStream : IDisposable
{
	private const ulong XVD_HEADER_INCL_SIGNATURE_SIZE = 12288uL;

	public readonly BinaryReader Reader;

	public FileStream XvdFileStream;

	private ulong HashTreePageCount;

	private ulong HashTreePageOffset;

	private ulong MutableDataOffset;

	private bool DataIntegrity;

	private bool Resiliency;

	private ulong HashTreeLevels;

	private ulong XvdUserDataOffset;

	private SegmentMetadataHeader SegmentMetadataHeaders;

	private SegmentsAbout[] Segments = Array.Empty<SegmentsAbout>();

	private string[] _segmentPaths = Array.Empty<string>();

	public XvcInfo XvcInfo;

	private XvcRegionHeader[] XvcRegions = Array.Empty<XvcRegionHeader>();

	private XvcUpdateSegment[] XvcUpdateSegments = Array.Empty<XvcUpdateSegment>();

	private XvcRegionSpecifier[] XvcRegionSpecifiers = Array.Empty<XvcRegionSpecifier>();

	private UserDataHeader UserDataHeader;

	private UserDataPackageFilesHeader UserDataPackageFiles;

	private readonly Dictionary<string, UserDataPackageFileEntry> UserDataPackages = new Dictionary<string, UserDataPackageFileEntry>();

	private readonly Dictionary<string, byte[]> UserDataPackageContents = new Dictionary<string, byte[]>();

	private int HashEntryLength;

	public string[] EncryptionKeys { get; private set; } = Array.Empty<string>();

	public MsiXVDHeader Header { get; private set; }

	public bool IsEncrypted { get; private set; }

	public MsiXVDStream(string fileUri)
	{
		if (!File.Exists(fileUri))
		{
			throw new FileNotFoundException("Can't found the file");
		}
		XvdFileStream = File.Open(fileUri, FileMode.Open, FileAccess.ReadWrite);
		Reader = new BinaryReader(XvdFileStream);
	}

	public void Parse()
	{
		XvdFileStream.Position = 0L;
		ParseFileHeader();
		Resiliency = Header.Volumes.HasFlag(MsiXVDVolumeAttributes.ResiliencyEnabled);
		DataIntegrity = !Header.Volumes.HasFlag(MsiXVDVolumeAttributes.DataIntegrityDisabled);
		HashTreePageCount = CalculateNumberHashPages(out HashTreeLevels, Header.NumberOfHashedPages, Resiliency);
		MutableDataOffset = Extensions.PageToOffset(Header.EmbeddedXvdPageCount) + 12288;
		HashTreePageOffset = Header.MutableDataLength + MutableDataOffset;
		XvdUserDataOffset = (DataIntegrity ? Extensions.PageToOffset(HashTreePageCount) : 0) + HashTreePageOffset;
		ParaseUserData();
		if (UserDataPackageContents.ContainsKey("SegmentMetadata.bin"))
		{
			ParseSegment();
		}
		ParseArea();
		List<string> list = new List<string>();
		for (int i = 0; i < XvcInfo.EncryptionKeyIds.Length; i++)
		{
			string text = new Guid(XvcInfo.EncryptionKeyIds[i].KeyId).ToString();
			if (text != "00000000-0000-0000-0000-000000000000")
			{
				list.Add(text);
			}
		}
		EncryptionKeys = list.ToArray();
	}

	private void ParseFileHeader()
	{
		int count = Marshal.SizeOf(typeof(MsiXVDHeader));
		Header = Extensions.GetstructFromBytes<MsiXVDHeader>(Reader.ReadBytes(count));
		IsEncrypted = !Header.Volumes.HasFlag(MsiXVDVolumeAttributes.EncryptionDisabled);
		HashEntryLength = (IsEncrypted ? 20 : 24);
	}

	private void ParaseUserData()
	{
		XvdFileStream.Position = (long)XvdUserDataOffset;
		byte[] array = new byte[Header.UserDataLength];
		XvdFileStream.ReadExactly(array);
		using BinaryReader binaryReader = new BinaryReader(new MemoryStream(array));
		UserDataHeader = Extensions.GetstructFromBytes<UserDataHeader>(binaryReader.ReadBytes(Marshal.SizeOf(typeof(UserDataHeader))));
		if (UserDataHeader.Type == UserDataType.PackageFiles)
		{
			binaryReader.BaseStream.Position = UserDataHeader.Length;
			UserDataPackageFiles = Extensions.GetstructFromBytes<UserDataPackageFilesHeader>(binaryReader.ReadBytes(Marshal.SizeOf(typeof(UserDataPackageFilesHeader))));
			int fileCount = (int)UserDataPackageFiles.FileCount;
			UserDataPackages.EnsureCapacity(fileCount);
			UserDataPackageFileEntry[] array2 = Extensions.GetstructArraysFromBytes<UserDataPackageFileEntry>(binaryReader.ReadBytes(Marshal.SizeOf(typeof(UserDataPackageFileEntry)) * fileCount), fileCount);
			UserDataPackageFileEntry[] array3 = array2;
			for (int i = 0; i < array3.Length; i++)
			{
				UserDataPackageFileEntry value = array3[i];
				binaryReader.BaseStream.Position = UserDataHeader.Length + value.Offset;
				byte[] array4 = new byte[value.Size];
				binaryReader.BaseStream.ReadExactly(array4);
				UserDataPackages[value.FilePath] = value;
				UserDataPackageContents[value.FilePath] = array4;
			}
		}
	}

	private void ParseArea()
	{
		ulong position = Extensions.PageToOffset(Header.UserDataPageCount) + XvdUserDataOffset;
		XvdFileStream.Position = (long)position;
		byte[] array = new byte[Header.XvcDataLength];
		XvdFileStream.ReadExactly(array);
		using BinaryReader binaryReader = new BinaryReader(new MemoryStream(array));
		XvcInfo = Extensions.GetstructFromBytes<XvcInfo>(binaryReader.ReadBytes(Marshal.SizeOf(typeof(XvcInfo))));
		if (XvcInfo.Version >= 1)
		{
			XvcRegions = Extensions.GetstructArraysFromBytes<XvcRegionHeader>(binaryReader.ReadBytes((int)(Marshal.SizeOf(typeof(XvcRegionHeader)) * XvcInfo.RegionCount)), XvcInfo.RegionCount);
			XvcUpdateSegments = Extensions.GetstructArraysFromBytes<XvcUpdateSegment>(binaryReader.ReadBytes((int)(Marshal.SizeOf(typeof(XvcUpdateSegment)) * XvcInfo.UpdateSegmentCount)), XvcInfo.UpdateSegmentCount);
			if (XvcInfo.Version >= 2)
			{
				XvcRegionSpecifiers = Extensions.GetstructArraysFromBytes<XvcRegionSpecifier>(binaryReader.ReadBytes((int)(Marshal.SizeOf(typeof(XvcRegionSpecifier)) * XvcInfo.RegionSpecifierCount)), XvcInfo.RegionSpecifierCount);
			}
		}
	}

	private void ParseSegment()
	{
		using BinaryReader binaryReader = new BinaryReader(new MemoryStream(UserDataPackageContents["SegmentMetadata.bin"]));
		SegmentMetadataHeaders = Extensions.GetstructFromBytes<SegmentMetadataHeader>(binaryReader.ReadBytes(Marshal.SizeOf(typeof(SegmentMetadataHeader))));
		Segments = Extensions.GetstructArraysFromBytes<SegmentsAbout>(binaryReader.ReadBytes(Marshal.SizeOf(typeof(SegmentsAbout)) * (int)SegmentMetadataHeaders.SegmentCount), SegmentMetadataHeaders.SegmentCount);
		_segmentPaths = new string[SegmentMetadataHeaders.SegmentCount];
		uint num = SegmentMetadataHeaders.HeaderLength + SegmentMetadataHeaders.SegmentCount * 16;
		for (int i = 0; i < Segments.Length; i++)
		{
			SegmentsAbout segmentsAbout = Segments[i];
			binaryReader.BaseStream.Position = num + segmentsAbout.PathOffset;
			Span<byte> span = binaryReader.ReadBytes(segmentsAbout.PathLength * 2).AsSpan();
			_segmentPaths[i] = new string(MemoryMarshal.Cast<byte, char>(span));
		}
	}

	private static ulong CalculateNumberHashPages(out ulong hashTreeLevels, ulong hashedPagesCount, bool resilient)
	{
		ulong num = (hashedPagesCount + 170 - 1) / 170;
		hashTreeLevels = 1uL;
		if (num > 1)
		{
			ulong num2 = 2uL;
			while (num2 > 1)
			{
				ulong num3 = hashTreeLevels;
				if (num3 > 3)
				{
					goto IL_00b0;
				}
				switch ((uint)num3)
				{
				case 0u:
					break;
				case 1u:
					goto IL_006e;
				case 2u:
					goto IL_0084;
				case 3u:
					goto IL_009a;
				default:
					goto IL_00b0;
				}
				ulong num4 = (hashedPagesCount + 170 - 1) / 170;
				goto IL_00b6;
				IL_0084:
				num4 = (hashedPagesCount + 4913000 - 1) / 4913000;
				goto IL_00b6;
				IL_006e:
				num4 = (hashedPagesCount + 28900 - 1) / 28900;
				goto IL_00b6;
				IL_00b6:
				ulong num5 = num4;
				num2 = num5;
				hashTreeLevels++;
				num += num5;
				continue;
				IL_00b0:
				num4 = 0uL;
				goto IL_00b6;
				IL_009a:
				num4 = (hashedPagesCount + 835210000 - 1) / 835210000;
				goto IL_00b6;
			}
		}
		if (resilient)
		{
			num *= 2;
		}
		return num;
	}

	public Task ExtractTaskAsync(string output, MsiXVDDecoder decoder, IProgress<DecompressProgress>? progress = null, CancellationToken cts = default(CancellationToken))
	{
		return Task.Run(delegate
		{
			ulong firstSegmentOffset = Extensions.PageToOffset(XvcUpdateSegments[0].PageNum);
			XvcRegionHeader[] array = XvcRegions.Where((XvcRegionHeader region) => region.FirstSegmentIndex != 0 || firstSegmentOffset == region.Offset).ToArray();
			XvcRegionHeader[] array2 = array;
			for (int num = 0; num < array2.Length; num++)
			{
				XvcRegionHeader xvcRegionHeader = array2[num];
				if (cts.IsCancellationRequested)
				{
					break;
				}
				ExtractPart(progress, output, decoder, (uint)xvcRegionHeader.Id, xvcRegionHeader.Offset, xvcRegionHeader.Length, xvcRegionHeader.FirstSegmentIndex, IsEncrypted && xvcRegionHeader.KeyId != ushort.MaxValue, cts);
			}
		}, cts);
	}

	private ulong CalculateHashEntryBlockOffset(ulong blockNo, out ulong hashEntryId)
	{
		ulong sourceUlong = Extensions.ComputeHashBlockIndexForDataBlock(Header.Kind, HashTreeLevels, Header.NumberOfHashedPages, blockNo, 0u, out hashEntryId, Resiliency);
		return HashTreePageOffset + Extensions.PageToOffset(sourceUlong);
	}

	private void ExtractPart(IProgress<DecompressProgress>? progressTask, string outputDirectory, MsiXVDDecoder decryptor, uint headerId, ulong regionStartOffset, ulong regionLength, uint startSegmentIndex, bool shouldDecrypt, CancellationToken cts)
	{
		Span<byte> span = stackalloc byte[16];
		if (shouldDecrypt)
		{
			MemoryMarshal.Cast<byte, uint>(span)[1] = headerId;
			Header.VdUid.AsSpan(0, 8).CopyTo(span.Slice(8));
		}
		bool flag = true;
		long num = (long)regionStartOffset;
		int num2 = 0;
		Span<byte> buffer = new byte[1048576];
		bool flag2 = DataIntegrity;
		long num3 = (long)CalculateHashEntryBlockOffset(Extensions.GetPageOffset(regionStartOffset - XvdUserDataOffset), out var hashEntryId);
		int num4 = (int)(hashEntryId * 24);
		Span<byte> buffer2 = new byte[1048576];
		uint num5 = startSegmentIndex;
		int num6 = 0;
		long pageOffset = (long)Extensions.GetPageOffset(regionLength);
		while (Segments.Length > num5 && pageOffset > num6 && !cts.IsCancellationRequested)
		{
			ulong fileSize = Segments[num5].FileSize;
			string text = _segmentPaths[num5];
			string path = Path.Join(outputDirectory, text);
			string directoryName = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			using FileStream fileStream = File.OpenWrite(path);
			ulong num7 = fileSize;
			do
			{
				int num8 = (int)Math.Min(num7, 4096uL);
				if (flag2)
				{
					XvdFileStream.Position = num3;
					XvdFileStream.ReadExactly(buffer2);
					flag2 = false;
				}
				if (flag)
				{
					XvdFileStream.Position = num;
					XvdFileStream.ReadExactly(buffer);
					flag = false;
				}
				Span<byte> span2 = buffer.Slice(num2, 4096);
				if (DataIntegrity)
				{
					Span<byte> span3 = buffer2.Slice(num4, 24);
					if (shouldDecrypt)
					{
						MemoryMarshal.Cast<byte, uint>(span)[0] = MemoryMarshal.Cast<byte, uint>(span3.Slice(HashEntryLength, 4))[0];
					}
					num4 += 24;
					hashEntryId++;
					if (hashEntryId == 170)
					{
						hashEntryId = 0uL;
						num4 += 16;
					}
					if (num4 == buffer2.Length)
					{
						num3 += num4;
						num4 = 0;
						hashEntryId = 0uL;
						flag2 = true;
					}
				}
				if (shouldDecrypt)
				{
					decryptor.Decrypt(span2, span2, span);
				}
				fileStream.Write(span2.Slice(0, num8));
				num7 -= (uint)num8;
				num2 += 4096;
				if (num2 == buffer.Length)
				{
					num += num2;
					num2 = 0;
					flag = true;
				}
				num6++;
				progressTask?.Report(new DecompressProgress
				{
					CurrentCount = num5,
					FileName = text,
					TotalCount = Segments.Length
				});
			}
			while (num7 != 0);
			num5++;
		}
	}

	public void Dispose()
	{
		XvdFileStream.Dispose();
		GC.Collect();
	}
}

using System;
using System.Collections.Generic;
using System.Text;
using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageSectionHeader : AbstractStructure
{
        public ImageSectionHeader(IRawFile peFile, long offset, ulong imageBaseAddress)
        : base(peFile, offset)
    {
        ImageBaseAddress = imageBaseAddress;
    }

        public ulong ImageBaseAddress { get; }

        public string Name
    {
        get
        {
            var s = PeFile.AsSpan(Offset, 8);
            return Encoding.UTF8.GetString(s).TrimEnd((char)0);
        }
        set
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > 8)
                throw new ArgumentOutOfRangeException("Section name has to be at max. 8 bytes.");
            PeFile.WriteBytes(Offset, bytes);
        }
    }


        public uint VirtualSize
    {
        get => PeFile.ReadUInt(Offset + 0x8);
        set => PeFile.WriteUInt(Offset + 0x8, value);
    }


        public uint VirtualAddress
    {
        get => PeFile.ReadUInt(Offset + 0xC);
        set => PeFile.WriteUInt(Offset + 0xC, value);
    }

        public uint SizeOfRawData
    {
        get => PeFile.ReadUInt(Offset + 0x10);
        set => PeFile.WriteUInt(Offset + 0x10, value);
    }

        public uint PointerToRawData
    {
        get => PeFile.ReadUInt(Offset + 0x14);
        set => PeFile.WriteUInt(Offset + 0x14, value);
    }

        public uint PointerToRelocations
    {
        get => PeFile.ReadUInt(Offset + 0x18);
        set => PeFile.WriteUInt(Offset + 0x18, value);
    }

        public uint PointerToLinenumbers
    {
        get => PeFile.ReadUInt(Offset + 0x1C);
        set => PeFile.WriteUInt(Offset + 0x1C, value);
    }

        public ushort NumberOfRelocations
    {
        get => PeFile.ReadUShort(Offset + 0x20);
        set => PeFile.WriteUShort(Offset + 0x20, value);
    }

        public ushort NumberOfLinenumbers
    {
        get => PeFile.ReadUShort(Offset + 0x22);
        set => PeFile.WriteUShort(Offset + 0x22, value);
    }

        public ScnCharacteristicsType Characteristics
    {
        get => (ScnCharacteristicsType)PeFile.ReadUInt(Offset + 0x24);
        set => PeFile.WriteUInt(Offset + 0x24, (uint)value);
    }

        public List<string> CharacteristicsResolved => ResolveCharacteristics(Characteristics);

        public static List<string> ResolveCharacteristics(ScnCharacteristicsType sectionFlags)
    {
        var st = new List<string>();
#if NET5_0_OR_GREATER
        var values = Enum.GetValues<ScnCharacteristicsType>();
#else
        var values = (ScnCharacteristicsType[])Enum.GetValues(typeof(ScnCharacteristicsType));
#endif
        foreach (var flag in values)
            if ((sectionFlags & flag) == flag)
                st.Add(flag.ToString());

        return st;
    }

        public byte[] ToArray()
    {
        var rawData = new BufferFile(new byte[0x28]); 

        rawData.WriteBytes(0x00, Encoding.ASCII.GetBytes(Name));
        rawData.WriteUInt(0x08, VirtualSize);
        rawData.WriteUInt(0x0C, VirtualAddress);
        rawData.WriteUInt(0x10, SizeOfRawData);
        rawData.WriteUInt(0x14, PointerToRawData);
        rawData.WriteUInt(0x18, PointerToRelocations);
        rawData.WriteUInt(0x1C, PointerToLinenumbers);
        rawData.WriteUShort(0x20, NumberOfRelocations);
        rawData.WriteUShort(0x22, NumberOfLinenumbers);
        rawData.WriteUInt(0x24, (uint)Characteristics);

        return rawData.ToArray();
    }
}

[Flags]
public enum ScnCharacteristicsType : uint
{
        TypeNoPad = 0x00000008,

        CntCode = 0x00000020,

        CntInitializedData = 0x00000040,

        CntUninitializedData = 0x00000080,

        LnkOther = 0x00000100,

        LnkInfo = 0x00000200,

        LnkRemove = 0x00000800,

        LnkComdat = 0x00001000,

        NoDeferSpecExc = 0x00004000,

        Gprel = 0x00008000,

        MemFardata = 0x00008000,

        MemPurgeable = 0x00020000,

        Mem16Bit = 0x00020000,

        MemLocked = 0x00040000,

        MemPreload = 0x00080000,

        Align1Bytes = 0x00100000,

        Align2Bytes = 0x00200000,

        Align4Bytes = 0x00300000,

        Align8Bytes = 0x00400000,

        Align16Bytes = 0x00500000,

        Align32Bytes = 0x00600000,

        Align64Bytes = 0x00700000,

        Align128Bytes = 0x00800000,

        Align256Bytes = 0x00900000,

        Align512Bytes = 0x00A00000,

        Align1024Bytes = 0x00B00000,

        Align2048Bytes = 0x00C00000,

        Align4096Bytes = 0x00D00000,

        Align8192Bytes = 0x00E00000,

        AlignMask = 0x00F00000,

        LnkNrelocOvfl = 0x01000000,

        MemDiscardable = 0x02000000,

        MemNotCached = 0x04000000,

        MemNotPaged = 0x08000000,

        MemShared = 0x10000000,

        MemExecute = 0x20000000,

        MemRead = 0x40000000,

        MemWrite = 0x80000000
}
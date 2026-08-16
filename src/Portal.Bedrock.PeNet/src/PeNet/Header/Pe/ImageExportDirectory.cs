using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageExportDirectory : AbstractStructure
{
        public ImageExportDirectory(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

        public uint Characteristics
    {
        get => PeFile.ReadUInt(Offset);
        set => PeFile.WriteUInt(Offset, value);
    }

        public uint TimeDateStamp
    {
        get => PeFile.ReadUInt(Offset + 0x4);
        set => PeFile.WriteUInt(Offset + 0x4, value);
    }

        public ushort MajorVersion
    {
        get => PeFile.ReadUShort(Offset + 0x8);
        set => PeFile.WriteUShort(Offset + 0x8, value);
    }

        public ushort MinorVersion
    {
        get => PeFile.ReadUShort(Offset + 0xA);
        set => PeFile.WriteUShort(Offset + 0xA, value);
    }

        public uint Name
    {
        get => PeFile.ReadUInt(Offset + 0xC);
        set => PeFile.WriteUInt(Offset + 0xC, value);
    }

        public uint Base
    {
        get => PeFile.ReadUInt(Offset + 0x10);
        set => PeFile.WriteUInt(Offset + 0x10, value);
    }

        public uint NumberOfFunctions
    {
        get => PeFile.ReadUInt(Offset + 0x14);
        set => PeFile.WriteUInt(Offset + 0x14, value);
    }

        public uint NumberOfNames
    {
        get => PeFile.ReadUInt(Offset + 0x18);
        set => PeFile.WriteUInt(Offset + 0x18, value);
    }

        public uint AddressOfFunctions
    {
        get => PeFile.ReadUInt(Offset + 0x1C);
        set => PeFile.WriteUInt(Offset + 0x1C, value);
    }

        public uint AddressOfNames
    {
        get => PeFile.ReadUInt(Offset + 0x20);
        set => PeFile.WriteUInt(Offset + 0x20, value);
    }

        public uint AddressOfNameOrdinals
    {
        get => PeFile.ReadUInt(Offset + 0x24);
        set => PeFile.WriteUInt(Offset + 0x24, value);
    }
}
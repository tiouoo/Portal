using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageDelayImportDescriptor : AbstractStructure
{
        public ImageDelayImportDescriptor(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

        public uint GrAttrs
    {
        get => PeFile.ReadUInt(Offset);
        set => PeFile.WriteUInt(Offset, value);
    }

        public uint SzName
    {
        get => PeFile.ReadUInt(Offset + 0x4);
        set => PeFile.WriteUInt(Offset + 0x4, value);
    }

        public uint Phmod
    {
        get => PeFile.ReadUInt(Offset + 0x8);
        set => PeFile.WriteUInt(Offset + 0x8, value);
    }

        public uint PIat
    {
        get => PeFile.ReadUInt(Offset + 0xc);
        set => PeFile.WriteUInt(Offset + 0xc, value);
    }

        public uint PInt
    {
        get => PeFile.ReadUInt(Offset + 0x10);
        set => PeFile.WriteUInt(Offset + 0x10, value);
    }

        public uint PBoundIAT
    {
        get => PeFile.ReadUInt(Offset + 0x14);
        set => PeFile.WriteUInt(Offset + 0x14, value);
    }

        public uint PUnloadIAT
    {
        get => PeFile.ReadUInt(Offset + 0x18);
        set => PeFile.WriteUInt(Offset + 0x16, value);
    }

        public uint DwTimeStamp
    {
        get => PeFile.ReadUInt(Offset + 0x1c);
        set => PeFile.WriteUInt(Offset + 0x1c, value);
    }
}
using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageResourceDataEntry : AbstractStructure
{
        public ImageResourceDataEntry(IRawFile peFile, ImageResourceDirectoryEntry parent, long offset)
        : base(peFile, offset)
    {
        Parent = parent;
    }

        public ImageResourceDirectoryEntry Parent { get; }

        public uint OffsetToData
    {
        get => PeFile.ReadUInt(Offset);
        set => PeFile.WriteUInt(Offset, value);
    }

        public uint Size1
    {
        get => PeFile.ReadUInt(Offset + 0x4);
        set => PeFile.WriteUInt(Offset + 0x4, value);
    }

        public uint CodePage
    {
        get => PeFile.ReadUInt(Offset + 0x8);
        set => PeFile.WriteUInt(Offset + 0x8, value);
    }

        public uint Reserved
    {
        get => PeFile.ReadUInt(Offset + 0xC);
        set => PeFile.WriteUInt(Offset + 0xC, value);
    }
}
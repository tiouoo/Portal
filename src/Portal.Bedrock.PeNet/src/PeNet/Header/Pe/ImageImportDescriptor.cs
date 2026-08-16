using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageImportDescriptor : AbstractStructure
{
        public ImageImportDescriptor(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

        public uint OriginalFirstThunk
    {
        get => PeFile.ReadUInt(Offset);
        set => PeFile.WriteUInt(Offset, value);
    }

        public uint TimeDateStamp
    {
        get => PeFile.ReadUInt(Offset + 0x4);
        set => PeFile.WriteUInt(Offset + 0x4, value);
    }

        public uint ForwarderChain
    {
        get => PeFile.ReadUInt(Offset + 0x8);
        set => PeFile.WriteUInt(Offset + 0x8, value);
    }

        public uint Name
    {
        get => PeFile.ReadUInt(Offset + 0xC);
        set => PeFile.WriteUInt(Offset + 0xC, value);
    }

        public uint FirstThunk
    {
        get => PeFile.ReadUInt(Offset + 0x10);
        set => PeFile.WriteUInt(Offset + 0x10, value);
    }
}
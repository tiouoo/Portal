using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageBoundImportDescriptor : AbstractStructure
{
        public ImageBoundImportDescriptor(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

        public uint TimeDateStamp
    {
        get => PeFile.ReadUInt(Offset + 0);
        set => PeFile.WriteUInt(Offset + 0, value);
    }

        public ushort OffsetModuleName
    {
        get => PeFile.ReadUShort(Offset + 4);
        set => PeFile.WriteUShort(Offset + 2, value);
    }

        public ushort NumberOfModuleForwarderRefs
    {
        get => PeFile.ReadUShort(Offset + 6);
        set => PeFile.WriteUShort(Offset + 4, value);
    }
}
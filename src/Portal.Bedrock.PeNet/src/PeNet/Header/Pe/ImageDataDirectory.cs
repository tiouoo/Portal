using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageDataDirectory : AbstractStructure
{
        public ImageDataDirectory(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

        public uint VirtualAddress
    {
        get => PeFile.ReadUInt(Offset);
        set => PeFile.WriteUInt(Offset, value);
    }

        public uint Size
    {
        get => PeFile.ReadUInt(Offset + 0x4);
        set => PeFile.WriteUInt(Offset + 0x4, value);
    }
}

public enum DataDirectoryType
{
        Export = 0,

        Import = 1,

        Resource = 2,

        Exception = 3,

        Security = 4,

        BaseReloc = 5,

        Debug = 6,

        Copyright = 7,

        Globalptr = 8,

        TLS = 9,

        LoadConfig = 0xA,

        BoundImport = 0xB,

        IAT = 0xC,

        DelayImport = 0xD,

        ComDescriptor = 0xE,

        Reserved = 0xF
}
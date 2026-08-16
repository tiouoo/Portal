using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageFileHeader : AbstractStructure
{
    public ImageFileHeader(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

    public ushort NumberOfSections
    {
        get => PeFile.ReadUShort(Offset + 0x2);
        set => PeFile.WriteUShort(Offset + 0x2, value);
    }

    public ushort SizeOfOptionalHeader
    {
        get => PeFile.ReadUShort(Offset + 0x10);
        set => PeFile.WriteUShort(Offset + 0x10, value);
    }
}

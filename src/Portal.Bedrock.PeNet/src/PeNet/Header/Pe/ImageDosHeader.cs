using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageDosHeader : AbstractStructure
{
    public ImageDosHeader(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

    public uint E_lfanew
    {
        get => PeFile.ReadUInt(Offset + 0x3C);
        set => PeFile.WriteUInt(Offset + 0x3C, value);
    }
}

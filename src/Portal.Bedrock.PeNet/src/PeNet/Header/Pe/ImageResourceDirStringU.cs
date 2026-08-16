using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageResourceDirStringU : AbstractStructure
{
        public ImageResourceDirStringU(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

        public ushort Length
    {
        get => PeFile.ReadUShort(Offset);
        set => PeFile.WriteUShort(Offset, value);
    }

        public string NameString => PeFile.ReadUnicodeString(Offset + 2, Length);
}
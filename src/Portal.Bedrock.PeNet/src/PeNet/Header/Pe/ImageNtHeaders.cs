using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageNtHeaders : AbstractStructure
{
        public readonly ImageFileHeader FileHeader;

        public readonly ImageOptionalHeader OptionalHeader;

        public ImageNtHeaders(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
        FileHeader = new ImageFileHeader(peFile, offset + 0x4);
        OptionalHeader = new ImageOptionalHeader(peFile, offset + 0x18, peFile.Is64Bit());
    }

        public uint Signature
    {
        get => PeFile.ReadUInt(Offset);
        set => PeFile.WriteUInt(Offset, value);
    }
}
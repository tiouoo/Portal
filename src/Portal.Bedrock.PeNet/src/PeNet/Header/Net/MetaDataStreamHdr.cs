using PeNet.FileParser;

namespace PeNet.Header.Net;

public class MetaDataStreamHdr : AbstractStructure
{
        public MetaDataStreamHdr(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

    internal uint HeaderLength => GetHeaderLength();

        public uint RelOffset
    {
        get => PeFile.ReadUInt(Offset);
        set => PeFile.WriteUInt(Offset, value);
    }

        public uint Size
    {
        get => PeFile.ReadUInt(Offset + 0x4);
        set => PeFile.WriteUInt(Offset + 0x4, value);
    }

        public string StreamName => PeFile.ReadAsciiString(Offset + 0x8);

    private uint GetHeaderLength()
    {
        var maxHeaderLength = 100;
        var headerLength = 0;
        for (var inHdrOffset = 8; inHdrOffset < maxHeaderLength; inHdrOffset++)
            if (PeFile.ReadByte(Offset + inHdrOffset) == 0x00)
            {
                headerLength = inHdrOffset;
                break;
            }

        return (uint)AddHeaderPaddingLength(headerLength);
    }

    private int AddHeaderPaddingLength(int headerLength)
    {
        if (headerLength % 4 == 0)
            return headerLength + 4;
        return headerLength + (4 - headerLength % 4);
    }
}
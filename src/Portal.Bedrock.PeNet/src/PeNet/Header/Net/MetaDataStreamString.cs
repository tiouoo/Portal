using PeNet.FileParser;

namespace PeNet.Header.Net;

public class MetaDataStreamString : AbstractStructure
{
    private readonly uint _size;

    public MetaDataStreamString(IRawFile peFile, long offset, uint size)
        : base(peFile, offset)
    {
        _size = size;
    }

        public string GetStringAtIndex(uint index)
    {
        return PeFile.ReadAsciiString(Offset + index);
    }
}
using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class Copyright : AbstractStructure
{
        public Copyright(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

        public string CopyrightString => PeFile.ReadAsciiString(Offset);
}
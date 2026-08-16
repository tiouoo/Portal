using PeNet.FileParser;

namespace PeNet.Header;

public abstract class AbstractStructure
{
        internal readonly long Offset;

        internal readonly IRawFile PeFile;
        
        protected AbstractStructure(IRawFile peFile, long offset)
    {
        PeFile = peFile;
        Offset = offset;
    }
}
using System.Collections.Generic;
using PeNet.FileParser;
using PeNet.Header.Pe;

namespace PeNet.HeaderParser.Pe;

internal class ImageImportDescriptorsParser : SafeParser<ImageImportDescriptor[]>
{
    public ImageImportDescriptorsParser(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

    protected override ImageImportDescriptor[]? ParseTarget()
    {
        if (Offset == 0)
            return null;

        var idescs = new List<ImageImportDescriptor>();
        uint idescSize = 20; 
        uint round = 0;

        while (true)
        {
            var idesc = new ImageImportDescriptor(PeFile, Offset + idescSize * round);

            
            if (idesc.OriginalFirstThunk == 0
                && idesc.ForwarderChain == 0
                && idesc.Name == 0
                && idesc.FirstThunk == 0)
                break;

            idescs.Add(idesc);
            round++;
        }


        return idescs.ToArray();
    }
}
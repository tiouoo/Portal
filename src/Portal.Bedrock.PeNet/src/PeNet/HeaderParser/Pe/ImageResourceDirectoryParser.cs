using PeNet.FileParser;
using PeNet.Header.Pe;

namespace PeNet.HeaderParser.Pe;

internal class ImageResourceDirectoryParser : SafeParser<ImageResourceDirectory>
{
    private readonly long _resourceDirSize;

    internal ImageResourceDirectoryParser(IRawFile peFile, long offset, long size)
        : base(peFile, offset)
    {
        _resourceDirSize = size;
    }

    protected override ImageResourceDirectory? ParseTarget()
    {
        if (Offset == 0)
            return null;

        
        var root = new ImageResourceDirectory(PeFile, null, Offset, Offset, _resourceDirSize);

        
        
        
        if ((root.NumberOfIdEntries + root.NumberOfNameEntries) * 10 >= _resourceDirSize)
            return root;

        if (root.DirectoryEntries is null)
            return root;

        
        foreach (var de in root.DirectoryEntries)
        {
            de!.ResourceDirectory = new ImageResourceDirectory(
                PeFile,
                de,
                Offset + de.OffsetToDirectory,
                Offset,
                _resourceDirSize
            );

            var sndLevel = de?.ResourceDirectory?.DirectoryEntries;
            if (sndLevel is null)
                continue;

            
            foreach (var de2 in sndLevel)
            {
                de2!.ResourceDirectory = new ImageResourceDirectory(
                    PeFile,
                    de2,
                    Offset + de2.OffsetToDirectory,
                    Offset,
                    _resourceDirSize
                );

                var thrdLevel = de2?.ResourceDirectory?.DirectoryEntries;
                if (thrdLevel is null)
                    continue;


                
                foreach (var de3 in thrdLevel)
                    de3!.ResourceDataEntry = new ImageResourceDataEntry(PeFile, de3,
                        Offset + de3.OffsetToData);
            }
        }

        return root;
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using PeNet.FileParser;

namespace PeNet.Header.Net;

public class MetaDataStreamGuid : AbstractStructure
{
    private readonly uint _size;

    public MetaDataStreamGuid(IRawFile peFile, long offset, uint size)
        : base(peFile, offset)
    {
        _size = size;
        GuidsAndIndices = ParseGuidsAndIndices();
        Guids = GuidsAndIndices.Select(x => x.Item1).ToList();
    }

        public List<Guid> Guids { get; }

        public List<Tuple<Guid, uint>> GuidsAndIndices { get; }

        public Guid? GetGuidAtIndex(uint index)
    {
        return GuidsAndIndices.FirstOrDefault(x => x.Item2 == index)?.Item1;
    }

    private List<Tuple<Guid, uint>> ParseGuidsAndIndices()
    {
        
        var numOfGuiDs = _size / 16;
        var guidsAndIndicies = new List<Tuple<Guid, uint>>((int)numOfGuiDs);

        for (var i = Offset; i < Offset + _size; i += 16)
        {
#if NET48 || NETSTANDARD2_0
                guidsAndIndicies.Add(new Tuple<Guid, uint>(new Guid(PeFile.AsSpan(i, 16).ToArray()), (uint)guidsAndIndicies.Count + 1));
#else
            guidsAndIndicies.Add(
                new Tuple<Guid, uint>(new Guid(PeFile.AsSpan(i, 16)), (uint)guidsAndIndicies.Count + 1));
#endif
        }

        return guidsAndIndicies;
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using PeNet.FileParser;

namespace PeNet.Header.Net;

public class MetaDataStreamUs : AbstractStructure
{
    private readonly uint _size;

    public MetaDataStreamUs(IRawFile peFile, long offset, uint size)
        : base(peFile, offset)
    {
        _size = size;
        UserStringsAndIndices = ParseUserStringsAndIndices();
        UserStrings = UserStringsAndIndices.Select(x => x.Item1).ToList();
    }

        public List<string> UserStrings { get; }

        public List<Tuple<string, uint>> UserStringsAndIndices { get; }

        public string? GetUserStringAtIndex(uint index)
    {
        return UserStringsAndIndices.FirstOrDefault(x => x.Item2 == index)?.Item1;
    }

    private List<Tuple<string, uint>> ParseUserStringsAndIndices()
    {
        var stringsAndIncides = new List<Tuple<string, uint>>();

        
        
        for (var i = Offset + 1; i < Offset + _size; i++)
        {
            if (PeFile.ReadByte(i) >= 0x80) 
                i++;

            int length = PeFile.ReadByte(i);

            if (length == 0) 
                break; 

            i += 1; 
            var tmpString = PeFile.ReadUnicodeString(i); 
            i += (uint)length - 1; 

            stringsAndIncides.Add(new Tuple<string, uint>(tmpString, (uint)i - (uint)length - (uint)Offset));
        }

        return stringsAndIncides;
    }
}
using System;
using System.Collections.Generic;
using PeNet.FileParser;

namespace PeNet.Header.Resource;

public class GroupIconDirectory : AbstractStructure
{
    private readonly long _sizeInBytes;
    private List<GroupIconDirectoryEntry>? _directoryEntries;
    private bool _entriesParsed;

        public GroupIconDirectory(IRawFile peFile, ResourceLocation location)
        : base(peFile, location.Offset)
    {
        _sizeInBytes = location.Size;
    }

        public ushort IdReserved
    {
        get => PeFile.ReadUShort(Offset);
        set => PeFile.WriteUShort(Offset, value);
    }

        public ushort IdType
    {
        get => PeFile.ReadUShort(Offset + 0x2);
        set => PeFile.WriteUShort(Offset + 0x2, value);
    }

        public ushort IdCount
    {
        get => PeFile.ReadUShort(Offset + 0x4);
        set => PeFile.WriteUShort(Offset + 0x4, value);
    }

        public IEnumerable<GroupIconDirectoryEntry> DirectoryEntries
    {
        get
        {
            if (_entriesParsed)
                return _directoryEntries.OrEmpty();

            _entriesParsed = true;
            return _directoryEntries = ParseDirectoryEntries();
        }
    }

    private List<GroupIconDirectoryEntry> ParseDirectoryEntries()
    {
        var numEntries = IdCount;
        var currentOffset = Offset + 0x6;
        var maxOffset = Math.Min(PeFile.Length, Offset + _sizeInBytes);
        if (currentOffset + numEntries * GroupIconDirectoryEntry.Size > maxOffset)
            
            numEntries = (ushort)((maxOffset - currentOffset) / GroupIconDirectoryEntry.Size);
        var parsedArray = new List<GroupIconDirectoryEntry>();
        for (ushort i = 0; i < numEntries; ++i)
        {
            parsedArray.Add(new GroupIconDirectoryEntry(PeFile, currentOffset));
            currentOffset += GroupIconDirectoryEntry.Size;
        }

        return parsedArray;
    }
}
using System;
using System.Collections.Generic;
using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageResourceDirectory : AbstractStructure
{
    private readonly long _resourceDirLength;
    private readonly long _resourceDirOffset;
    private List<ImageResourceDirectoryEntry?>? _directoryEntries;
    private bool _entriesParsed;

        public ImageResourceDirectory(IRawFile peFile, ImageResourceDirectoryEntry? parent, long offset,
        long resourceDirOffset, long resourceDirLength)
        : base(peFile, offset)
    {
        Parent = parent;
        _resourceDirOffset = resourceDirOffset;
        _resourceDirLength = resourceDirLength;
    }

        public List<ImageResourceDirectoryEntry?>? DirectoryEntries
    {
        get
        {
            if (_entriesParsed)
                return _directoryEntries;

            _entriesParsed = true;
            _directoryEntries = ParseDirectoryEntries(_resourceDirOffset);
            return _directoryEntries;
        }
    }

        public ImageResourceDirectoryEntry? Parent { get; }

        public uint Characteristics
    {
        get => PeFile.ReadUInt(Offset);
        set => PeFile.WriteUInt(Offset, value);
    }

        public uint TimeDateStamp
    {
        get => PeFile.ReadUInt(Offset + 0x4);
        set => PeFile.WriteUInt(Offset + 0x4, value);
    }

        public ushort MajorVersion
    {
        get => PeFile.ReadUShort(Offset + 0x8);
        set => PeFile.WriteUShort(Offset + 0x8, value);
    }

        public ushort MinorVersion
    {
        get => PeFile.ReadUShort(Offset + 0xa);
        set => PeFile.WriteUShort(Offset + 0xa, value);
    }

        public ushort NumberOfNameEntries
    {
        get => PeFile.ReadUShort(Offset + 0xc);
        set => PeFile.WriteUShort(Offset + 0xc, value);
    }

        public ushort NumberOfIdEntries
    {
        get => PeFile.ReadUShort(Offset + 0xe);
        set => PeFile.WriteUShort(Offset + 0xe, value);
    }

    private List<ImageResourceDirectoryEntry?> ParseDirectoryEntries(long resourceDirOffset)
    {
        var numEntries = NumberOfIdEntries + NumberOfNameEntries;

        var entries = new List<ImageResourceDirectoryEntry?>(numEntries);

        for (var index = 0; index < numEntries; index++)
            try
            {
                var entry = new ImageResourceDirectoryEntry(PeFile, this, (uint)index * 8 + Offset + 16,
                    resourceDirOffset);

                if (SanityCheckFailed(entry))
                    break;
                entries.Add(entry);
            }
            catch (IndexOutOfRangeException)
            {
                break;
            }

        return entries;
    }

    private bool SanityCheckFailed(ImageResourceDirectoryEntry? rd)
    {
        if (rd == null)
            return true;

        if (rd.IsNamedEntry && rd.NameResolved == null)
            return true;

        if (rd.IsNamedEntry && rd.NameResolved == "unknown")
            return true;

        if (rd.OffsetToDirectory > _resourceDirLength)
            return true;

        return false;
    }
}
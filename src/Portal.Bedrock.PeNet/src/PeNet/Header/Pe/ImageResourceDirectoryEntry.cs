using System;
using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageResourceDirectoryEntry : AbstractStructure
{
        public ImageResourceDirectoryEntry(IRawFile peFile, ImageResourceDirectory parent, long offset,
        long resourceDirOffset)
        : base(peFile, offset)
    {
        Parent = parent;

        
        try
        {
            if (IsIdEntry)
            {
                NameResolved = ResolveResourceId(ID);
            }
            else if (IsNamedEntry)
            {
                var nameAddress = resourceDirOffset + (Name & 0x7FFFFFFF);
                var unicodeName = new ImageResourceDirStringU(PeFile, nameAddress);
                NameResolved = unicodeName.NameString;
            }
        }
        catch (Exception)
        {
            NameResolved = null;
        }
    }

        public ImageResourceDirectory Parent { get; }

        public ImageResourceDirectory? ResourceDirectory { get; internal set; }

        public ImageResourceDataEntry? ResourceDataEntry { get; internal set; }

        public uint Name
    {
        get => PeFile.ReadUInt(Offset);
        set => PeFile.WriteUInt(Offset, value);
    }

        public string? NameResolved { get; }

        public uint ID
    {
        get => Name & 0xFFFF;
        set => Name = value & 0xFFFF;
    }

        public uint OffsetToData
    {
        get => PeFile.ReadUInt(Offset + 0x4);
        set => PeFile.WriteUInt(Offset + 0x4, value);
    }

        public uint OffsetToDirectory => OffsetToData & 0x7FFFFFFF;

        public bool DataIsDirectory
    {
        get
        {
            if ((OffsetToData & 0x80000000) == 0x80000000)
                return true;
            return false;
        }
    }

        public bool IsNamedEntry
    {
        get
        {
            if ((Name & 0x80000000) == 0x80000000)
                return true;
            return false;
        }
    }

        public bool IsIdEntry => !IsNamedEntry;

        public static string ResolveResourceId(uint id)
    {
        return id switch
        {
            (uint)ResourceGroupIdType.Cursor => "Cursor",
            (uint)ResourceGroupIdType.Bitmap => "Bitmap",
            (uint)ResourceGroupIdType.Icon => "Icon",
            (uint)ResourceGroupIdType.Menu => "Menu",
            (uint)ResourceGroupIdType.Dialog => "Dialog",
            (uint)ResourceGroupIdType.String => "String",
            (uint)ResourceGroupIdType.FontDirectory => "FontDirectory",
            (uint)ResourceGroupIdType.Font => "Font",
            (uint)ResourceGroupIdType.Accelerator => "Accelerator",
            (uint)ResourceGroupIdType.RcData => "RcData",
            (uint)ResourceGroupIdType.MessageTable => "MessageTable",
            (uint)ResourceGroupIdType.GroupIcon => "GroupIcon",
            (uint)ResourceGroupIdType.GroupCursor => "GroupCursor",
            (uint)ResourceGroupIdType.Version => "Version",
            (uint)ResourceGroupIdType.DlgInclude => "DlgInclude",
            (uint)ResourceGroupIdType.PlugAndPlay => "PlugAndPlay",
            (uint)ResourceGroupIdType.VXD => "VXD",
            (uint)ResourceGroupIdType.AnimatedCursor => "AnimatedCursor",
            (uint)ResourceGroupIdType.AnimatedIcon => "AnimatedIcon",
            (uint)ResourceGroupIdType.HTML => "HTML",
            (uint)ResourceGroupIdType.Manifest => "Manifest",
            _ => "unknown"
        };
    }
}

public enum ResourceGroupIdType : uint
{
        Cursor = 1,

        Bitmap = 2,

        Icon = 3,

        Menu = 4,

        Dialog = 5,

        String = 6,

        FontDirectory = 7,

        Font = 8,

        Accelerator = 9,

        RcData = 10,

        MessageTable = 11,

        GroupCursor = 12,

        GroupIcon = 14,

        Version = 16,

        DlgInclude = 17,

        PlugAndPlay = 19,

        VXD = 20,

        AnimatedCursor = 21,

        AnimatedIcon = 22,

        HTML = 23,

        Manifest = 24
}
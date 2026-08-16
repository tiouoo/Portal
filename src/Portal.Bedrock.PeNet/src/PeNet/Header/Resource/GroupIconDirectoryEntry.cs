using System.Collections.Generic;
using System.Linq;
using PeNet.FileParser;

namespace PeNet.Header.Resource;

public class GroupIconDirectoryEntry : AbstractStructure
{
    public const ushort Size = 0xE;

        public GroupIconDirectoryEntry(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

        public byte BWidth
    {
        get => PeFile.ReadByte(Offset);
        set => PeFile.WriteByte(Offset, value);
    }

        public byte BHeight
    {
        get => PeFile.ReadByte(Offset + 0x1);
        set => PeFile.WriteByte(Offset + 0x1, value);
    }

        public byte BColorCount
    {
        get => PeFile.ReadByte(Offset + 0x2);
        set => PeFile.WriteByte(Offset + 0x2, value);
    }

        public byte BReserved
    {
        get => PeFile.ReadByte(Offset + 0x3);
        set => PeFile.WriteByte(Offset + 0x3, value);
    }

        public ushort WPlanes
    {
        get => PeFile.ReadUShort(Offset + 0x4);
        set => PeFile.WriteUShort(Offset + 0x4, value);
    }

        public ushort WBitCount
    {
        get => PeFile.ReadUShort(Offset + 0x6);
        set => PeFile.WriteUShort(Offset + 0x6, value);
    }

        public uint DwBytesInRes
    {
        get => PeFile.ReadUInt(Offset + 0x8);
        set => PeFile.WriteUInt(Offset + 0x8, value);
    }

        public ushort NId
    {
        get => PeFile.ReadUShort(Offset + 0x0C);
        set => PeFile.WriteUShort(Offset + 0x0C, value);
    }

        public IEnumerable<Icon>? AssociatedIcons(PeFile peFile)
    {
        return peFile.Resources?.Icons?.Where(i => i.Id == NId);
    }
}
using System.Collections.Generic;
using PeNet.FileParser;

namespace PeNet.Header.Resource;

public class StringFileInfo : AbstractStructure
{
    private StringTable[]? _stringTable;

        public StringFileInfo(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

        public ushort WLength
    {
        get => PeFile.ReadUShort(Offset);
        set => PeFile.WriteUShort(Offset, value);
    }

        public ushort WValueLength
    {
        get => PeFile.ReadUShort(Offset + 0x2);
        set => PeFile.WriteUShort(Offset + 0x2, value);
    }

        public ushort WType
    {
        get => PeFile.ReadUShort(Offset + 0x4);
        set => PeFile.WriteUShort(Offset + 0x4, value);
    }

        public string SzKey => PeFile.ReadUnicodeString(Offset + 0x6);

        public StringTable[] StringTable
    {
        get
        {
            _stringTable ??= ReadChildren();
            return _stringTable;
        }
    }

    private StringTable[] ReadChildren()
    {
        var currentOffset =
            Offset + 6 + SzKey.UStringByteLength()
            + (Offset + 6 + SzKey.UStringByteLength()).PaddingBytes(32);

        var children = new List<StringTable>();

        while (currentOffset < Offset + WLength)
        {
            var st = new StringTable(PeFile, currentOffset);

            if (st.WLength == 0)
                break;

            currentOffset += st.WLength;
            children.Add(st);
        }

        return children.ToArray();
    }
}
using System.Collections.Generic;
using PeNet.FileParser;

namespace PeNet.Header.Resource;

public class VarFileInfo : AbstractStructure
{
    private Var[]? _children;

        public VarFileInfo(IRawFile peFile, long offset)
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
        set => PeFile.WriteUShort(Offset + 04, value);
    }

        public string SzKey => PeFile.ReadUnicodeString(Offset + 0x6);


        public Var[] Children
    {
        get
        {
            _children ??= ReadChildren();
            return _children;
        }
    }

    private Var[] ReadChildren()
    {
        var currentOffset =
            Offset + 6 + SzKey.UStringByteLength()
            + (Offset + 6 + SzKey.UStringByteLength()).PaddingBytes(32);

        var values = new List<Var>();

        while (currentOffset < Offset + WLength)
        {
            var v = new Var(PeFile, currentOffset);
            if (v.WLength == 0)
                break;

            currentOffset += v.WLength;
            values.Add(v);
        }

        return values.ToArray();
    }
}
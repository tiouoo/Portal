using System.Collections.Generic;
using PeNet.FileParser;

namespace PeNet.Header.Resource;

public class Var : AbstractStructure
{
    private uint[]? _value;

        public Var(IRawFile peFile, long offset)
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

        public uint[] Value
    {
        get
        {
            _value ??= ReadValues();
            return _value;
        }
    }

    private uint[] ReadValues()
    {
        var currentOffset =
            Offset + 6 + SzKey.UStringByteLength()
            + (Offset + 6 + SzKey.UStringByteLength()).PaddingBytes(32);

        var startOfValues = currentOffset;

        var values = new List<uint>();

        while (currentOffset < startOfValues + WValueLength)
        {
            values.Add(PeFile.ReadUInt(currentOffset));
            currentOffset += sizeof(uint);
        }

        return values.ToArray();
    }
}
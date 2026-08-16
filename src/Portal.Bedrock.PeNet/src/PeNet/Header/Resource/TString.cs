using PeNet.FileParser;

namespace PeNet.Header.Resource;

public class TString : AbstractStructure
{
        public TString(IRawFile peFile, long offset)
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

        public string Value
    {
        get
        {
            var currentOffset = Offset + 0x6 + SzKey.UStringByteLength() +
                                (Offset + 0x6 + SzKey.UStringByteLength()).PaddingBytes(32);

            return PeFile.ReadUnicodeString(currentOffset);
        }
    }
}
using PeNet.FileParser;

namespace PeNet.Header.Resource;

public class VsVersionInfo : AbstractStructure
{
    private StringFileInfo? _stringFileInfo;
    private VarFileInfo? _varFileInfo;
    private VsFixedFileInfo? _vsFixedFileInfo;

        public VsVersionInfo(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

    private uint VsFixedFileInfoOffset =>
        (uint)(Offset + 6 + SzKey.UStringByteLength()
               + (Offset + 6 + SzKey.UStringByteLength()).PaddingBytes(32));

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

        public VsFixedFileInfo VsFixedFileInfo
    {
        get
        {
            var currentOffset = VsFixedFileInfoOffset;

            _vsFixedFileInfo ??= new VsFixedFileInfo(PeFile, (int)currentOffset);

            return _vsFixedFileInfo;
        }
    }

        public StringFileInfo StringFileInfo
    {
        get
        {
            var isFirst = IsStringFileInfoFirstChild();

            var currentOffset = VsFixedFileInfoOffset;
            currentOffset += WValueLength;
            currentOffset += currentOffset.PaddingBytes(32);

            if (!isFirst)
            {
                currentOffset += VarFileInfo.WLength;
                currentOffset += currentOffset.PaddingBytes(32);
            }


            _stringFileInfo ??= new StringFileInfo(PeFile, currentOffset);

            return _stringFileInfo;
        }
    }

        public VarFileInfo VarFileInfo
    {
        get
        {
            var notFirst = IsStringFileInfoFirstChild();

            var currentOffset = VsFixedFileInfoOffset;
            currentOffset += WValueLength;
            currentOffset += currentOffset.PaddingBytes(32);

            if (notFirst)
            {
                currentOffset += StringFileInfo.WLength;
                currentOffset += currentOffset.PaddingBytes(32);
            }

            _varFileInfo ??= new VarFileInfo(PeFile, currentOffset);

            return _varFileInfo;
        }
    }

    private bool IsStringFileInfoFirstChild()
    {
        var currentOffset = VsFixedFileInfoOffset;
        currentOffset += WValueLength;
        currentOffset += currentOffset.PaddingBytes(32);
        currentOffset += 6;

        var readMarker = PeFile.ReadUnicodeString(currentOffset);

        return readMarker == "StringFileInfo";
    }
}
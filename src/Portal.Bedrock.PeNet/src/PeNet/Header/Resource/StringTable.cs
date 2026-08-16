using System.Collections.Generic;
using System.Linq;
using PeNet.FileParser;

namespace PeNet.Header.Resource;

public class StringTable : AbstractStructure
{
    private TString[]? _children;

        public StringTable(IRawFile peFile, long offset)
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

        public TString[] String
    {
        get
        {
            _children ??= ReadChildren();
            return _children;
        }
    }

        public string? Comments => GetValue(nameof(Comments));

        public string? CompanyName => GetValue(nameof(CompanyName));

        public string? FileDescription => GetValue(nameof(FileDescription));

        public string? FileVersion => GetValue(nameof(FileVersion));

        public string? InternalName => GetValue(nameof(InternalName));

        public string? LegalCopyright => GetValue(nameof(LegalCopyright));

        public string? LegalTrademarks => GetValue(nameof(LegalTrademarks));

        public string? OriginalFilename => GetValue(nameof(OriginalFilename));

        public string? PrivateBuild => GetValue(nameof(PrivateBuild));

        public string? ProductName => GetValue(nameof(ProductName));

        public string? ProductVersion => GetValue(nameof(ProductVersion));

        public string? SpecialBuild => GetValue(nameof(SpecialBuild));

    private string? GetValue(string value)
    {
        return String.FirstOrDefault(s => s.SzKey == value)?.Value;
    }

    private TString[] ReadChildren()
    {
        var currentOffset = Offset + 6 + (SzKey.Length * 2 + 2) +
                            (Offset + 6 + (SzKey.Length * 2 + 2)).PaddingBytes(32);
        var children = new List<TString>();

        while (currentOffset < Offset + WLength)
        {
            currentOffset += currentOffset.PaddingBytes(32);

            var ts = new TString(PeFile, currentOffset);
            if (ts.WLength == 0)
                break;

            currentOffset += ts.WLength;
            children.Add(ts);
        }

        return children.ToArray();
    }
}
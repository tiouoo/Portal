using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageOptionalHeader : AbstractStructure
{
    private readonly bool _is64Bit;

    public readonly ImageDataDirectory[] DataDirectory;

    public ImageOptionalHeader(IRawFile peFile, long offset, bool is64Bit)
        : base(peFile, offset)
    {
        _is64Bit = is64Bit;

        DataDirectory = new ImageDataDirectory[16];

        var dataDirOffset = _is64Bit ? 0x70 : 0x60;

        for (uint i = 0; i < 16; i++)
            DataDirectory[i] = new ImageDataDirectory(peFile, offset + dataDirOffset + i * 0x8);
    }

    public ulong ImageBase
    {
        get =>
            _is64Bit
                ? PeFile.ReadULong(Offset + 0x18)
                : PeFile.ReadUInt(Offset + 0x1C);
        set
        {
            if (!_is64Bit)
                PeFile.WriteUInt(Offset + 0x1C, (uint)value);
            else
                PeFile.WriteULong(Offset + 0x18, value);
        }
    }

    public uint SectionAlignment
    {
        get => PeFile.ReadUInt(Offset + 0x20);
        set => PeFile.WriteUInt(Offset + 0x20, value);
    }

    public uint FileAlignment
    {
        get => PeFile.ReadUInt(Offset + 0x24);
        set => PeFile.WriteUInt(Offset + 0x24, value);
    }

    public uint SizeOfImage
    {
        get => PeFile.ReadUInt(Offset + 0x38);
        set => PeFile.WriteUInt(Offset + 0x38, value);
    }
}

public enum MagicType : ushort
{
    Bit32 = 0x10b,

    Bit64 = 0x20b,

    Rom = 0x107
}

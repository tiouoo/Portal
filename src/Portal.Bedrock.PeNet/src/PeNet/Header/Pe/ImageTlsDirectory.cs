using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageTlsDirectory : AbstractStructure
{
    private readonly bool _is64Bit;

        public ImageTlsDirectory(IRawFile peFile, long offset, bool is64Bit)
        : base(peFile, offset)
    {
        _is64Bit = is64Bit;
    }

        public ulong StartAddressOfRawData
    {
        get => _is64Bit ? PeFile.ReadULong(Offset + 0) : PeFile.ReadUInt(Offset + 0);
        set
        {
            if (_is64Bit)
                PeFile.WriteULong(Offset + 0, value);
            else
                PeFile.WriteUInt(Offset + 0, (uint)value);
        }
    }

        public ulong EndAddressOfRawData
    {
        get => _is64Bit ? PeFile.ReadULong(Offset + 8) : PeFile.ReadUInt(Offset + 4);
        set
        {
            if (_is64Bit)
                PeFile.WriteULong(Offset + 8, value);
            else
                PeFile.WriteUInt(Offset + 4, (uint)value);
        }
    }

        public ulong AddressOfIndex
    {
        get => _is64Bit ? PeFile.ReadULong(Offset + 0x10) : PeFile.ReadUInt(Offset + 8);
        set
        {
            if (_is64Bit)
                PeFile.WriteULong(Offset + 0x10, value);
            else
                PeFile.WriteUInt(Offset + 8, (uint)value);
        }
    }

        public ulong AddressOfCallBacks
    {
        get => _is64Bit ? PeFile.ReadULong(Offset + 0x18) : PeFile.ReadUInt(Offset + 0x0c);
        set
        {
            if (_is64Bit)
                PeFile.WriteULong(Offset + 0x18, value);
            else
                PeFile.WriteUInt(Offset + 0x0c, (uint)value);
        }
    }

        public uint SizeOfZeroFill
    {
        get => _is64Bit ? PeFile.ReadUInt(Offset + 0x20) : PeFile.ReadUInt(Offset + 0x10);
        set
        {
            if (_is64Bit)
                PeFile.WriteUInt(Offset + 0x20, value);
            else
                PeFile.WriteUInt(Offset + 0x10, value);
        }
    }

        public uint Characteristics
    {
        get => _is64Bit ? PeFile.ReadUInt(Offset + 0x24) : PeFile.ReadUInt(Offset + 0x14);
        set
        {
            if (_is64Bit)
                PeFile.WriteUInt(Offset + 0x24, value);
            else
                PeFile.WriteUInt(Offset + 0x14, value);
        }
    }

        public ImageTlsCallback[]? TlsCallbacks { get; set; }
}
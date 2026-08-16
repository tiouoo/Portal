using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageThunkData : AbstractStructure
{
    private readonly bool _is64Bit;

        public ImageThunkData(IRawFile peFile, uint offset, bool is64Bit)
        : base(peFile, offset)
    {
        _is64Bit = is64Bit;
    }

        public ulong AddressOfData
    {
        get => _is64Bit ? PeFile.ReadULong(Offset) : PeFile.ReadUInt(Offset);
        set
        {
            if (!_is64Bit)
                PeFile.WriteUInt(Offset, (uint)value);
            else
                PeFile.WriteULong(Offset, value);
        }
    }

        public ulong Ordinal
    {
        get => AddressOfData;
        set => AddressOfData = value;
    }

        public ulong ForwarderString
    {
        get => AddressOfData;
        set => AddressOfData = value;
    }

        public ulong Function
    {
        get => AddressOfData;
        set => AddressOfData = value;
    }
}
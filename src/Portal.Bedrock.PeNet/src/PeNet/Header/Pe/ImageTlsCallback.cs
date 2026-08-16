using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageTlsCallback : AbstractStructure
{
    private readonly bool _is64Bit;

        public ImageTlsCallback(IRawFile peFile, long offset, bool is64Bit)
        : base(peFile, offset)
    {
        _is64Bit = is64Bit;
    }

        public ulong Callback
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
}
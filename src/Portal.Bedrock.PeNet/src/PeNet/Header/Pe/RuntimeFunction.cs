using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class RuntimeFunction : AbstractStructure
{
    private readonly ImageSectionHeader[] _sectionHeaders;
    private UnwindInfo? _resolvedUnwindInfo;

        public RuntimeFunction(IRawFile peFile, long offset, ImageSectionHeader[] sh)
        : base(peFile, offset)
    {
        _sectionHeaders = sh;
    }

        public uint FunctionStart
    {
        get => PeFile.ReadUInt(Offset);
        set => PeFile.WriteUInt(Offset, value);
    }

        public uint FunctionEnd
    {
        get => PeFile.ReadUInt(Offset + 0x4);
        set => PeFile.WriteUInt(Offset + 0x4, value);
    }

        public uint UnwindInfo
    {
        get => PeFile.ReadUInt(Offset + 0x8);
        set => PeFile.WriteUInt(Offset + 0x8, value);
    }

        public UnwindInfo ResolvedUnwindInfo
    {
        get
        {
            _resolvedUnwindInfo ??= GetUnwindInfo(_sectionHeaders);
            return _resolvedUnwindInfo;
        }
    }

        private UnwindInfo GetUnwindInfo(ImageSectionHeader[] sh)
    {
        
        
        var uwAddress = (UnwindInfo & 0x1) == 0x1
            ? UnwindInfo & 0xFFFE
            : UnwindInfo;

        var uw = new UnwindInfo(PeFile, uwAddress.RvaToOffset(sh));
        return uw;
    }
}
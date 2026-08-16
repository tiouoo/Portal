using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class UnwindCode : AbstractStructure
{
        public UnwindCode(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

        public byte CodeOffset
    {
        get => PeFile.ReadByte(Offset);
        set => PeFile.WriteByte(Offset, value);
    }

        public UnwindOpType UnwindOp
        => (UnwindOpType)(PeFile.ReadByte(Offset + 0x1) >> 4);

        public byte Opinfo
        => (byte)(PeFile.ReadByte(Offset + 0x1) & 0xF);

        public ushort FrameOffset
    {
        get => PeFile.ReadUShort(Offset + 0x2);
        set => PeFile.WriteUShort(Offset + 0x2, value);
    }
}

public enum UnwindOpType : byte
{
        PushNonvol = 0,

        AllocLarge = 1,

        AllocSmall = 2,

        SetFpreg = 3,

        SaveNonvol = 4,

        SaveNonvolFar = 5,

        SaveXmm128 = 8,

        SaveXmm128Far = 9,

        PushMachframe = 10
}
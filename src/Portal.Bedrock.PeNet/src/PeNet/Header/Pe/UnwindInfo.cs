using System.Collections.Generic;
using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class UnwindInfo : AbstractStructure
{
    private const int SizeOfUnwindCode = 0x2;

        public UnwindInfo(IRawFile peFile, uint offset)
        : base(peFile, offset)
    {
    }

        public byte Version => (byte)(PeFile.ReadByte(Offset) & 0x1F);

        public byte Flags => (byte)(PeFile.ReadByte(Offset) >> 5);

        public byte SizeOfProlog
    {
        get => PeFile.ReadByte(Offset + 0x1);
        set => PeFile.WriteByte(Offset + 0x1, value);
    }

        public byte CountOfCodes
    {
        get => PeFile.ReadByte(Offset + 0x2);
        set => PeFile.WriteByte(Offset + 0x2, value);
    }

        public int EvenCountOfCodes => (CountOfCodes + 1) / 2 * 2;

        public byte FrameRegister => (byte)(PeFile.ReadByte(Offset + 0x3) >> 4);

        public byte FrameOffset => (byte)(PeFile.ReadByte(Offset + 0x3) & 0xF);

        public UnwindCode[] UnwindCode => ParseUnwindCodes(PeFile, Offset + 0x4);

        public uint ExceptionHandler
    {
        get
        {
            var off = (uint)(Offset + 0x4 + SizeOfUnwindCode * EvenCountOfCodes);
            return PeFile.ReadUInt(off);
        }
        set
        {
            var off = (uint)(Offset + 0x4 + SizeOfUnwindCode * EvenCountOfCodes);
            PeFile.WriteUInt(off, value);
        }
    }

        public uint FunctionEntry
    {
        get => ExceptionHandler;
        set => ExceptionHandler = value;
    }

    private UnwindCode[] ParseUnwindCodes(IRawFile peFile, long offset)
    {
        var ucList = new List<UnwindCode>();
        var i = 0;
        const uint nodeSize = 0x2;
        var currentUnwindCode = offset;
        while (i < CountOfCodes)
        {
            int numberOfNodes;
            var uw = new UnwindCode(peFile, currentUnwindCode);
            currentUnwindCode += nodeSize; 

            switch (uw.UnwindOp)
            {
                case UnwindOpType.PushNonvol:
                    break;
                case UnwindOpType.AllocLarge:
                    currentUnwindCode += (uint)(uw.Opinfo == 0 ? 0x2 : 0x4);
                    break;
                case UnwindOpType.AllocSmall:
                    break;
                case UnwindOpType.SetFpreg:
                    break;
                case UnwindOpType.SaveNonvol:
                    currentUnwindCode += 0x2;
                    break;
                case UnwindOpType.SaveNonvolFar:
                    currentUnwindCode += 0x4;
                    break;
                case UnwindOpType.SaveXmm128:
                    currentUnwindCode += 0x2;
                    break;
                case UnwindOpType.SaveXmm128Far:
                    currentUnwindCode += 0x4;
                    break;
                case UnwindOpType.PushMachframe:
                    break;
            }

            if ((uw.UnwindOp == UnwindOpType.AllocLarge
                 && uw.Opinfo == 0x0)
                || uw.UnwindOp == UnwindOpType.SaveNonvol
                || uw.UnwindOp == UnwindOpType.SaveXmm128)
                numberOfNodes = 2;
            else if ((uw.UnwindOp == UnwindOpType.AllocLarge
                      && uw.Opinfo == 0x1)
                     || uw.UnwindOp == UnwindOpType.SaveNonvolFar
                     || uw.UnwindOp == UnwindOpType.SaveXmm128Far)
                numberOfNodes = 3;
            else
                numberOfNodes = 1;

            i += numberOfNodes;

            ucList.Add(uw);
        }

        return ucList.ToArray();
    }
}
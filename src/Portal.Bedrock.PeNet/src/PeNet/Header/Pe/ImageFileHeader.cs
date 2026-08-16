using System;
using System.Collections.Generic;
using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageFileHeader : AbstractStructure
{
        public ImageFileHeader(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

        public MachineType Machine
    {
        get => (MachineType)PeFile.ReadUShort(Offset);
        set => PeFile.WriteUShort(Offset, (ushort)value);
    }

        public string MachineResolved => ResolveMachine(Machine);

        public ushort NumberOfSections
    {
        get => PeFile.ReadUShort(Offset + 0x2);
        set => PeFile.WriteUShort(Offset + 0x2, value);
    }

        public uint TimeDateStamp
    {
        get => PeFile.ReadUInt(Offset + 0x4);
        set => PeFile.WriteUInt(Offset + 0x4, value);
    }

        public uint PointerToSymbolTable
    {
        get => PeFile.ReadUInt(Offset + 0x8);
        set => PeFile.WriteUInt(Offset + 0x8, value);
    }

        public uint NumberOfSymbols
    {
        get => PeFile.ReadUInt(Offset + 0xC);
        set => PeFile.WriteUInt(Offset + 0xC, value);
    }

        public ushort SizeOfOptionalHeader
    {
        get => PeFile.ReadUShort(Offset + 0x10);
        set => PeFile.WriteUShort(Offset + 0x10, value);
    }

        public FileCharacteristicsType Characteristics
    {
        get => (FileCharacteristicsType)PeFile.ReadUShort(Offset + 0x12);
        set => PeFile.WriteUShort(Offset + 0x12, (ushort)value);
    }

        public List<string> CharacteristicsResolved
        => ResolveFileCharacteristics(Characteristics);

        public static List<string> ResolveFileCharacteristics(FileCharacteristicsType characteristics)
    {
        var st = new List<string>();
#if NET5_0_OR_GREATER
        var values = Enum.GetValues<FileCharacteristicsType>();
#else
        var values = (FileCharacteristicsType[])Enum.GetValues(typeof(FileCharacteristicsType));
#endif
        foreach (var flag in values)
            if ((characteristics & flag) == flag)
                st.Add(flag.ToString());

        return st;
    }

        public static string ResolveMachine(MachineType targetMachine)
    {
        return targetMachine switch
        {
            MachineType.I386 => "Intel 386",
            MachineType.I860 => "Intel i860",
            MachineType.R3000 => "MIPS R3000",
            MachineType.R4000 => "MIPS little endian (R4000)",
            MachineType.R10000 => "MIPS R10000",
            MachineType.Wcemipsv2 => "MIPS little endian WCI v2",
            MachineType.OldAlpha => "old Alpha AXP",
            MachineType.Alpha => "Alpha AXP",
            MachineType.Sh3 => "Hitachi SH3",
            MachineType.Sh3Dsp => "Hitachi SH3 DSP",
            MachineType.Sh3E => "Hitachi SH3E",
            MachineType.Sh4 => "Hitachi SH4",
            MachineType.Sh5 => "Hitachi SH5",
            MachineType.Arm => "ARM little endian",
            MachineType.Thumb => "Thumb",
            MachineType.Am33 => "Matsushita AM33",
            MachineType.PowerPc => "PowerPC little endian",
            MachineType.PowerPcFp => "PowerPC with floating point support",
            MachineType.Ia64 => "Intel IA64",
            MachineType.Mips16 => "MIPS16",
            MachineType.M68K => "Motorola 68000 series",
            MachineType.Alpha64 => "Alpha AXP 64-bit",
            MachineType.MipsFpu => "MIPS with FPU",
            MachineType.TriCore => "Tricore",
            MachineType.Cef => "CEF",
            MachineType.MipsFpu16 => "MIPS16 with FPU",
            MachineType.Ebc => "EFI Byte Code",
            MachineType.Amd64 => "AMD64",
            MachineType.M32R => "Mitsubishi M32R little endian",
            MachineType.Cee => "clr pure MSIL",
            MachineType.Arm64 => "ARM64 Little-Endian",
            MachineType.ArmNt => "ARM Thumb-2 Little-Endian",
            MachineType.TargetHost => "Interacts with the host and not a WOW64 guest",
            MachineType.LinuxDotnet64 => "Linux .NET x64",
            MachineType.LinuxDotnet32 => "Linux .NET x86",
            MachineType.OsXDotnet64 => "Mac OS .NET x64",
            MachineType.OsXDotnet32 => "Mac OS .NET x86",
            MachineType.FreeBSDDotnet64 => "FreeBSD .NET x64",
            MachineType.FreeBSDDotnet32 => "FreeBSD .NET x86",
            MachineType.NetBSDDotnet64 => "NetBSD .NET x64",
            MachineType.NetBSDDotnet32 => "NetBSD .NET x86",
            MachineType.SunDotnet64 => "Sun .NET x64",
            MachineType.SunDotnet32 => "Sun .NET x86",
            MachineType.RiscV32 => "RISC-V 32-bit address space",
            MachineType.RiscV64 => "RISC-V 64-bit address space",
            MachineType.RiscV128 => "RISC-V 128-bit address space",

            _ => "unknown"
        };
    }
}

[Flags]
public enum FileCharacteristicsType : ushort
{
        RelocsStripped = 0x01,

        ExecutableImage = 0x02,

        LineNumsStripped = 0x04,

        LocalSymsStripped = 0x08,

        AggresiveWsTrim = 0x10,

        LargeAddressAware = 0x20,

        BytesReversedLo = 0x80,

        BitMachine32 = 0x100,

        DebugStripped = 0x200,

        RemovableRunFromSwap = 0x400,

        NetRunFromSwap = 0x800,

        System = 0x1000,

        Dll = 0x2000,

        UpSystemOnly = 0x4000,

        BytesReversedHi = 0x8000
}

[Flags]
public enum MachineType : ushort
{
        Unknown = 0x0,

        I386 = 0x14c,

        I860 = 0x14d,

        R3000 = 0x162,

        R4000 = 0x166,

        R10000 = 0x168,

        Wcemipsv2 = 0x169,

        OldAlpha = 0x183,

        Alpha = 0x184,

        Sh3 = 0x1a2,

        Sh3Dsp = 0x1a3,

        Sh3E = 0x1a4,

        Sh4 = 0x1a6,

        Sh5 = 0x1a8,

        Arm = 0x1c0,

        Thumb = 0x1c2,

        Am33 = 0x1d3,

        PowerPc = 0x1f0,

        PowerPcFp = 0x1f1,

        Ia64 = 0x200,

        Mips16 = 0x266,

        M68K = 0x268,

        Alpha64 = 0x284,

        MipsFpu = 0x366,

        MipsFpu16 = 0x466,

        Axp64 = Alpha64,

        TriCore = 0x520,

        Cef = 0xcef,

        Ebc = 0xebc,

        Amd64 = 0x8664,

        M32R = 0x9041,

        Cee = 0xc0ee,

        Arm64 = 0xAA64,

        ArmNt = 0x01C4,

        TargetHost = 0x0001,

        RiscV32 = 0x5032,

        RiscV64 = 0x5064,

        RiscV128 = 0x5128,

        LoongArch32 = 0x6232,

        LoongArch64 = 0x6264,

        LinuxDotnet64 = Amd64 ^ 0x4644, 

        OsXDotnet64 = Amd64 ^ 0x7B79, 

        FreeBSDDotnet64 = Amd64 ^ 0xADC4, 

        NetBSDDotnet64 = Amd64 ^ 0x1993, 

        SunDotnet64 = Amd64 ^ 0x1992, 

        LinuxDotnet32 = I386 ^ 0x4644, 

        OsXDotnet32 = I386 ^ 0x7B79, 

        FreeBSDDotnet32 = I386 ^ 0xADC4, 

        NetBSDDotnet32 = I386 ^ 0x1993, 

        SunDotnet32 = I386 ^ 0x1992 
}
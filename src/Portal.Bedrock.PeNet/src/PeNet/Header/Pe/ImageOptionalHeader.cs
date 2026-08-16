using System;
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

        public MagicType Magic
    {
        get => (MagicType)PeFile.ReadUShort(Offset);
        set => PeFile.WriteUShort(Offset, (ushort)value);
    }

        public byte MajorLinkerVersion
    {
        get => PeFile.ReadByte(Offset + 0x2);
        set => PeFile.WriteByte(Offset + 0x2, value);
    }

        public byte MinorLinkerVersion
    {
        get => PeFile.ReadByte(Offset + 0x3);
        set => PeFile.WriteByte(Offset + 03, value);
    }

        public uint SizeOfCode
    {
        get => PeFile.ReadUInt(Offset + 0x4);
        set => PeFile.WriteUInt(Offset + 0x4, value);
    }

        public uint SizeOfInitializedData
    {
        get => PeFile.ReadUInt(Offset + 0x8);
        set => PeFile.WriteUInt(Offset + 0x8, value);
    }

        public uint SizeOfUninitializedData
    {
        get => PeFile.ReadUInt(Offset + 0xC);
        set => PeFile.WriteUInt(Offset + 0xC, value);
    }

        public uint AddressOfEntryPoint
    {
        get => PeFile.ReadUInt(Offset + 0x10);
        set => PeFile.WriteUInt(Offset + 0x10, value);
    }

        public uint BaseOfCode
    {
        get => PeFile.ReadUInt(Offset + 0x14);
        set => PeFile.WriteUInt(Offset + 0x14, value);
    }

        public uint BaseOfData
    {
        get => _is64Bit ? 0 : PeFile.ReadUInt(Offset + 0x18);
        set
        {
            if (!_is64Bit)
                PeFile.WriteUInt(Offset + 0x18, value);
            else
                throw new Exception("ImageOptionalHeader->BaseOfCode does not exist in 64 bit applications.");
        }
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

        public ushort MajorOperatingSystemVersion
    {
        get => PeFile.ReadUShort(Offset + 0x28);
        set => PeFile.WriteUShort(Offset + 0x28, value);
    }

        public ushort MinorOperatingSystemVersion
    {
        get => PeFile.ReadUShort(Offset + 0x2A);
        set => PeFile.WriteUShort(Offset + 0x2A, value);
    }

        public ushort MajorImageVersion
    {
        get => PeFile.ReadUShort(Offset + 0x2C);
        set => PeFile.WriteUShort(Offset + 0x2C, value);
    }

        public ushort MinorImageVersion
    {
        get => PeFile.ReadUShort(Offset + 0x2E);
        set => PeFile.WriteUShort(Offset + 0x2E, value);
    }

        public ushort MajorSubsystemVersion
    {
        get => PeFile.ReadUShort(Offset + 0x30);
        set => PeFile.WriteUShort(Offset + 0x30, value);
    }

        public ushort MinorSubsystemVersion
    {
        get => PeFile.ReadUShort(Offset + 0x32);
        set => PeFile.WriteUShort(Offset + 0x32, value);
    }

        public uint Win32VersionValue
    {
        get => PeFile.ReadUInt(Offset + 0x34);
        set => PeFile.WriteUInt(Offset + 0x34, value);
    }

        public uint SizeOfImage
    {
        get => PeFile.ReadUInt(Offset + 0x38);
        set => PeFile.WriteUInt(Offset + 0x38, value);
    }

        public uint SizeOfHeaders
    {
        get => PeFile.ReadUInt(Offset + 0x3C);
        set => PeFile.WriteUInt(Offset + 0x3C, value);
    }

        public uint CheckSum
    {
        get => PeFile.ReadUInt(Offset + 0x40);
        set => PeFile.WriteUInt(Offset + 0x40, value);
    }

        public SubsystemType Subsystem
    {
        get => (SubsystemType)PeFile.ReadUShort(Offset + 0x44);
        set => PeFile.WriteUShort(Offset + 0x44, (ushort)value);
    }

        public string SubsystemResolved => ResolveSubsystem(Subsystem);

        public DllCharacteristicsType DllCharacteristics
    {
        get => (DllCharacteristicsType)PeFile.ReadUShort(Offset + 0x46);
        set => PeFile.WriteUShort(Offset + 0x46, (ushort)value);
    }

        public ulong SizeOfStackReserve
    {
        get =>
            _is64Bit
                ? PeFile.ReadULong(Offset + 0x48)
                : PeFile.ReadUInt(Offset + 0x48);
        set
        {
            if (!_is64Bit)
                PeFile.WriteUInt(Offset + 0x48, (uint)value);
            else
                PeFile.WriteULong(Offset + 0x48, value);
        }
    }

        public ulong SizeOfStackCommit
    {
        get =>
            _is64Bit
                ? PeFile.ReadULong(Offset + 0x50)
                : PeFile.ReadUInt(Offset + 0x4C);
        set
        {
            if (!_is64Bit)
                PeFile.WriteUInt(Offset + 0x4C, (uint)value);
            else
                PeFile.WriteULong(Offset + 0x50, value);
        }
    }

        public ulong SizeOfHeapReserve
    {
        get =>
            _is64Bit
                ? PeFile.ReadULong(Offset + 0x58)
                : PeFile.ReadUInt(Offset + 0x50);
        set
        {
            if (!_is64Bit)
                PeFile.WriteUInt(Offset + 0x50, (uint)value);
            else
                PeFile.WriteULong(Offset + 0x58, value);
        }
    }

        public ulong SizeOfHeapCommit
    {
        get =>
            _is64Bit
                ? PeFile.ReadULong(Offset + 0x60)
                : PeFile.ReadUInt(Offset + 0x54);
        set
        {
            if (!_is64Bit)
                PeFile.WriteUInt(Offset + 0x54, (uint)value);
            else
                PeFile.WriteULong(Offset + 0x60, value);
        }
    }

        public uint LoaderFlags
    {
        get =>
            _is64Bit
                ? PeFile.ReadUInt(Offset + 0x68)
                : PeFile.ReadUInt(Offset + 0x58);
        set
        {
            if (!_is64Bit)
                PeFile.WriteUInt(Offset + 0x58, value);
            else
                PeFile.WriteUInt(Offset + 0x68, value);
        }
    }

        public uint NumberOfRvaAndSizes
    {
        get =>
            _is64Bit
                ? PeFile.ReadUInt(Offset + 0x6C)
                : PeFile.ReadUInt(Offset + 0x5C);
        set
        {
            if (!_is64Bit)
                PeFile.WriteUInt(Offset + 0x5C, value);
            else
                PeFile.WriteUInt(Offset + 0x6C, value);
        }
    }

        public static string ResolveSubsystem(SubsystemType subsystem)
    {
        return subsystem switch
        {
            SubsystemType.Unknown => "Unknown Subsystem",
            SubsystemType.Native => "Native",
            SubsystemType.WindowsGui => "Windows GUI",
            SubsystemType.WindowsCui => "Windows CUI",
            SubsystemType.Os2Cui => "OS/2 CUI",
            SubsystemType.PosixCui => "POSIX CUI",
            SubsystemType.NativeWindows => "Native Windows",
            SubsystemType.WindowsCeGui => "Windows CE CUI",
            SubsystemType.EfiApplication => "EFI application",
            SubsystemType.EfiBootServiceDriver => "EFI boot service driver",
            SubsystemType.EfiRuntimeDriver => "EFI runtime service driver",
            SubsystemType.EfiRom => "EFI ROM image",
            SubsystemType.Xbox => "XBox",
            SubsystemType.WindowsBootApplication => "Windows boot application",
            _ => "Unknown Subsystem"
        };
    }
}

public enum SubsystemType : ushort
{
    Unknown = 0,
    Native = 1,
    WindowsGui = 2,
    WindowsCui = 3,
    Os2Cui = 5,
    PosixCui = 7,
    NativeWindows = 8,
    WindowsCeGui = 9,
    EfiApplication = 10,
    EfiBootServiceDriver = 11,
    EfiRuntimeDriver = 12,
    EfiRom = 13,
    Xbox = 14,
    WindowsBootApplication = 16
}

[Flags]
public enum DllCharacteristicsType : ushort
{
        HighEntropyVA = 0x20,

        DynamicBase = 0x40,

        ForceIntegrity = 0x80,

        NxCompat = 0x100,

        NoIsolation = 0x200,

        NoSeh = 0x400,

        NoBind = 0x800,

        AppContainer = 0x1000,

        WdmDriver = 0x2000,

        GuardCF = 0x4000,

        TerminalServerAware = 0x8000
}

[Flags]
public enum MagicType : ushort
{
        Bit32 = 0x10b,

        Bit64 = 0x20b,

        Rom = 0x107
}
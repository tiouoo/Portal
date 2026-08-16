using System;
using PeNet.FileParser;
using PeNet.Header.Resource;

namespace PeNet.Header.Pe;

public class ImageDebugDirectory : AbstractStructure
{
    private CvInfoPdb70? _cvInfoPdb70;

        public ImageDebugDirectory(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

        public uint Characteristics
    {
        get => PeFile.ReadUInt(Offset);
        set => PeFile.WriteUInt(Offset, value);
    }

        public uint TimeDateStamp
    {
        get => PeFile.ReadUInt(Offset + 0x4);
        set => PeFile.WriteUInt(Offset + 0x4, value);
    }

        public ushort MajorVersion
    {
        get => PeFile.ReadUShort(Offset + 0x8);
        set => PeFile.WriteUShort(Offset + 0x8, value);
    }

        public ushort MinorVersion
    {
        get => PeFile.ReadUShort(Offset + 0xa);
        set => PeFile.WriteUShort(Offset + 0xa, value);
    }

        public uint Type
    {
        get => PeFile.ReadUInt(Offset + 0xc);
        set => PeFile.WriteUInt(Offset + 0xc, value);
    }

    public DebugDirectoryType DebugType
    {
        get => (DebugDirectoryType)Type;
        set => Type = (uint)value;
    }

        public uint SizeOfData
    {
        get => PeFile.ReadUInt(Offset + 0x10);
        set => PeFile.WriteUInt(Offset + 0x10, value);
    }

        public uint AddressOfRawData
    {
        get => PeFile.ReadUInt(Offset + 0x14);
        set => PeFile.WriteUInt(Offset + 0x14, value);
    }

        public uint PointerToRawData
    {
        get => PeFile.ReadUInt(Offset + 0x18);
        set => PeFile.WriteUInt(Offset + 0x18, value);
    }

        public CvInfoPdb70? CvInfoPdb70
    {
        get
        {
            if (DebugType != DebugDirectoryType.CodeView)
                return null;

            _cvInfoPdb70 ??= new CvInfoPdb70(
                PeFile,
                PointerToRawData);

            return _cvInfoPdb70;
        }
    }

        public ExtendedDllCharacteristicsType? ExtendedDllCharacteristics
    {
        get
        {
            if (DebugType != DebugDirectoryType.ExtendedDllCharacteristics)
                return null;

            return (ExtendedDllCharacteristicsType)PeFile.ReadUInt(PointerToRawData);
        }
    }
}

public enum DebugDirectoryType : uint
{
        Unknown = 0,

        Coff = 1,

        CodeView = 2,

        FramePointerOmission = 3,

        Misc = 4,

        Exception = 5,

        Fixup = 6,

        OMapToSource = 7,

        OMapFromSource = 8,

        Borland = 9,

        Reserved10 = 10,

        Clsid = 11,

        VcFeature = 12,

        Pogo = 13,

        Iltcg = 14,

        Mpx = 15,

        Reproducible = 16,

        EmbeddedPortablePdb = 17,

        Reserved18 = 18,

        PdbChecksum = 19,

        ExtendedDllCharacteristics = 20
}

[Flags]
public enum ExtendedDllCharacteristicsType : uint
{
        Unknown = 0x00,

        CetCompat = 0x01,

        CetCompatStrictMode = 0x02,

        CetSetContextIpValidationRelaxMod = 0x04,

        CetDynamicApisAllowInProc = 0x08,

        CetReserved1 = 0x10,

        CetReserved2 = 0x20
}
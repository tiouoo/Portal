using PeNet.FileParser;

namespace PeNet.Header.Resource;

public class VsFixedFileInfo : AbstractStructure
{
        public VsFixedFileInfo(IRawFile peFile, int offset)
        : base(peFile, offset)
    {
    }

        public uint DwSignature
    {
        get => PeFile.ReadUInt(Offset);
        set => PeFile.WriteUInt(Offset, value);
    }

        public uint DwStrucVersion
    {
        get => PeFile.ReadUInt(Offset + 0x4);
        set => PeFile.WriteUInt(Offset + 0x4, value);
    }

        public uint DwFileVersionMS
    {
        get => PeFile.ReadUInt(Offset + 0x8);
        set => PeFile.WriteUInt(Offset + 0x8, value);
    }

        public uint DwFileVersionLS
    {
        get => PeFile.ReadUInt(Offset + 0xC);
        set => PeFile.WriteUInt(Offset + 0xC, value);
    }

        public uint DwProductVersionMS
    {
        get => PeFile.ReadUInt(Offset + 0x10);
        set => PeFile.WriteUInt(Offset + 0x10, value);
    }

        public uint DwProductVersionLS
    {
        get => PeFile.ReadUInt(Offset + 0x14);
        set => PeFile.WriteUInt(Offset + 0x14, value);
    }

        public uint DwFileFlagsMask
    {
        get => PeFile.ReadUInt(Offset + 0x18);
        set => PeFile.WriteUInt(Offset + 0x18, value);
    }

        public uint DwFileFlags
    {
        get => PeFile.ReadUInt(Offset + 0x1C);
        set => PeFile.WriteUInt(Offset + 0x1C, value);
    }

        public uint DwFileOS
    {
        get => PeFile.ReadUInt(Offset + 0x20);
        set => PeFile.WriteUInt(Offset + 0x20, value);
    }

        public uint DwFileType
    {
        get => PeFile.ReadUInt(Offset + 0x24);
        set => PeFile.WriteUInt(Offset + 0x24, value);
    }

        public uint DwFileSubType
    {
        get => PeFile.ReadUInt(Offset + 0x28);
        set => PeFile.WriteUInt(Offset + 0x28, value);
    }

        public uint DwFileDateMS
    {
        get => PeFile.ReadUInt(Offset + 0x2C);
        set => PeFile.WriteUInt(Offset + 0x2C, value);
    }

        public uint DwFileDateLS
    {
        get => PeFile.ReadUInt(Offset + 0x30);
        set => PeFile.WriteUInt(Offset + 0x30, value);
    }
}
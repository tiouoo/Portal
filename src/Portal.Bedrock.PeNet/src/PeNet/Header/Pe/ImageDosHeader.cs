using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageDosHeader : AbstractStructure
{
        public ImageDosHeader(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

        public ushort E_magic
    {
        get => PeFile.ReadUShort(Offset + 0x00);
        set => PeFile.WriteUShort(Offset + 0x00, value);
    }

        public ushort E_cblp
    {
        get => PeFile.ReadUShort(Offset + 0x02);
        set => PeFile.WriteUShort(Offset + 0x02, value);
    }

        public ushort E_cp
    {
        get => PeFile.ReadUShort(Offset + 0x04);
        set => PeFile.WriteUShort(Offset + 0x04, value);
    }

        public ushort E_crlc
    {
        get => PeFile.ReadUShort(Offset + 0x06);
        set => PeFile.WriteUShort(Offset + 0x06, value);
    }

        public ushort E_cparhdr
    {
        get => PeFile.ReadUShort(Offset + 0x08);
        set => PeFile.WriteUShort(Offset + 0x08, value);
    }

        public ushort E_minalloc
    {
        get => PeFile.ReadUShort(Offset + 0x0A);
        set => PeFile.WriteUShort(Offset + 0x0A, value);
    }

        public ushort E_maxalloc
    {
        get => PeFile.ReadUShort(Offset + 0x0C);
        set => PeFile.WriteUShort(Offset + 0x0C, value);
    }

        public ushort E_ss
    {
        get => PeFile.ReadUShort(Offset + 0x0E);
        set => PeFile.WriteUShort(Offset + 0x0E, value);
    }

        public ushort E_sp
    {
        get => PeFile.ReadUShort(Offset + 0x10);
        set => PeFile.WriteUShort(Offset + 0x10, value);
    }

        public ushort E_csum
    {
        get => PeFile.ReadUShort(Offset + 0x12);
        set => PeFile.WriteUShort(Offset + 0x12, value);
    }

        public ushort E_ip
    {
        get => PeFile.ReadUShort(Offset + 0x14);
        set => PeFile.WriteUShort(Offset + 0x14, value);
    }

        public ushort E_cs
    {
        get => PeFile.ReadUShort(Offset + 0x16);
        set => PeFile.WriteUShort(Offset + 0x16, value);
    }

        public ushort E_lfarlc
    {
        get => PeFile.ReadUShort(Offset + 0x18);
        set => PeFile.WriteUShort(Offset + 0x18, value);
    }

        public ushort E_ovno
    {
        get => PeFile.ReadUShort(Offset + 0x1A);
        set => PeFile.WriteUShort(Offset + 0x1A, value);
    }

        public ushort[] E_res 
    {
        get
        {
            return new[]
            {
                PeFile.ReadUShort(Offset + 0x1C),
                PeFile.ReadUShort(Offset + 0x1E),
                PeFile.ReadUShort(Offset + 0x20),
                PeFile.ReadUShort(Offset + 0x22)
            };
        }
        set
        {
            PeFile.WriteUShort(Offset + 0x1C, value[0]);
            PeFile.WriteUShort(Offset + 0x1E, value[1]);
            PeFile.WriteUShort(Offset + 0x20, value[2]);
            PeFile.WriteUShort(Offset + 0x22, value[3]);
        }
    }

        public ushort E_oemid
    {
        get => PeFile.ReadUShort(Offset + 0x24);
        set => PeFile.WriteUShort(Offset + 0x24, value);
    }

        public ushort E_oeminfo
    {
        get => PeFile.ReadUShort(Offset + 0x26);
        set => PeFile.WriteUShort(Offset + 0x26, value);
    }

        public ushort[] E_res2 
    {
        get
        {
            return new[]
            {
                PeFile.ReadUShort(Offset + 0x28),
                PeFile.ReadUShort(Offset + 0x2A),
                PeFile.ReadUShort(Offset + 0x2C),
                PeFile.ReadUShort(Offset + 0x2E),
                PeFile.ReadUShort(Offset + 0x30),
                PeFile.ReadUShort(Offset + 0x32),
                PeFile.ReadUShort(Offset + 0x34),
                PeFile.ReadUShort(Offset + 0x36),
                PeFile.ReadUShort(Offset + 0x38),
                PeFile.ReadUShort(Offset + 0x3A)
            };
        }
        set
        {
            PeFile.WriteUShort(Offset + 0x28, value[0]);
            PeFile.WriteUShort(Offset + 0x2A, value[1]);
            PeFile.WriteUShort(Offset + 0x2C, value[2]);
            PeFile.WriteUShort(Offset + 0x2E, value[3]);
            PeFile.WriteUShort(Offset + 0x30, value[4]);
            PeFile.WriteUShort(Offset + 0x32, value[5]);
            PeFile.WriteUShort(Offset + 0x34, value[6]);
            PeFile.WriteUShort(Offset + 0x36, value[7]);
            PeFile.WriteUShort(Offset + 0x38, value[8]);
            PeFile.WriteUShort(Offset + 0x3A, value[9]);
        }
    }

        public uint E_lfanew
    {
        get => PeFile.ReadUInt(Offset + 0x3C);
        set => PeFile.WriteUInt(Offset + 0x3C, value);
    }
}
using System;
using PeNet.FileParser;

namespace PeNet.Header.Resource;

public class Icon : AbstractStructure
{
    private const uint IcoHeaderSize = 6;
    private const uint IcoDirectorySize = 16;
    private static readonly byte[] PNGHeader = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public Icon(IRawFile peFile, long offset, uint size, uint id, Resources parent)
        : base(peFile, offset)
    {
        Size = size;
        Id = id;
        Parent = parent;
    }

    public uint Size { get; }
    public uint Id { get; }
    private Resources Parent { get; }


    private bool IsPng => AsRawSpan().Length >= 8 && AsRawSpan().Slice(0, 8).SequenceEqual(PNGHeader);
    private bool IsTooShort => AsRawSpan().IsEmpty || AsRawSpan().Length <= 32;

        public Span<byte> AsRawSpan()
    {
        return PeFile.AsSpan(Offset, Size);
    }

        public byte[]? AsIco()
    {
        var raw = AsRawSpan();

        if (IsPng) return raw.ToArray(); 
        if (IsTooShort) return null;

        var header = GenerateIcoHeader();
        var directory = GenerateIcoDirectory();
        var icoBytes = new byte[header.Length + directory.Length + raw.Length];

        icoBytes.WriteBytes(0, header);
        icoBytes.WriteBytes(header.Length, directory);
        icoBytes.WriteBytes(header.Length + directory.Length, raw);
        return icoBytes;
    }

    private static byte[] GenerateIcoHeader()
    {
        var header = new byte[IcoHeaderSize];
        header.WriteBytes(0, ((ushort)0).LittleEndianBytes().AsSpan());
        header.WriteBytes(2, ((ushort)1).LittleEndianBytes().AsSpan());
        header.WriteBytes(4, ((ushort)1).LittleEndianBytes().AsSpan());
        return header;
    }

    private byte[] GenerateIcoDirectory()
    {
        var directory = new byte[IcoDirectorySize];
        directory[0] = AsRawSpan()[4]; 
        directory[1] =
            AsRawSpan()
                [4]; 

        directory[2] = AsRawSpan()[32]; 
        directory[3] = 0x00; 

        directory[4] = AsRawSpan()[12]; 
        directory[5] = 0x00;

        directory[6] = AsRawSpan()[14]; 
        directory[7] = 0x00;

        directory.WriteBytes(8, ((uint)AsRawSpan().Length).LittleEndianBytes()); 

        directory.WriteBytes(12, (IcoHeaderSize + IcoDirectorySize).LittleEndianBytes()); 
        return directory;
    }
}
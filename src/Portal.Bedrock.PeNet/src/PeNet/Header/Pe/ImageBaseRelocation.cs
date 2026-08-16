using System;
using System.Collections.Generic;
using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageBaseRelocation : AbstractStructure
{
        public ImageBaseRelocation(IRawFile peFile, long offset, uint relocSize)
        : base(peFile, offset)
    {
        if (SizeOfBlock > relocSize)
            throw new ArgumentOutOfRangeException(nameof(relocSize),
                "SizeOfBlock cannot be bigger than size of the Relocation Directory.");

        if (SizeOfBlock < 8)
            throw new Exception("SizeOfBlock cannot be smaller than 8.");

        ParseTypeOffsets();
    }

        public uint VirtualAddress
    {
        get => PeFile.ReadUInt(Offset);
        set => PeFile.WriteUInt(Offset, value);
    }

        public uint SizeOfBlock
    {
        get => PeFile.ReadUInt(Offset + 0x4);
        set => PeFile.WriteUInt(Offset + 0x4, value);
    }

        public TypeOffset[]? TypeOffsets { get; private set; }

    private void ParseTypeOffsets()
    {
        var list = new List<TypeOffset>();
        for (uint i = 0; i < (SizeOfBlock - 8) / 2; i++) list.Add(new TypeOffset(PeFile, Offset + 8 + i * 2));
        TypeOffsets = list.ToArray();
    }

        public class TypeOffset
    {
        private readonly long _offset;
        private readonly IRawFile _peFile;

                public TypeOffset(IRawFile peFile, long offset)
        {
            _peFile = peFile;
            _offset = offset;
        }

                public byte Type
        {
            get
            {
                var to = _peFile.ReadUShort(_offset);
                return (byte)(to >> 12);
            }
        }

                public ushort Offset
        {
            get
            {
                var to = _peFile.ReadUShort(_offset);
                return (ushort)(to & 0xFFF);
            }
        }
    }
}
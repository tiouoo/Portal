using System;
using System.Text;
using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageImportByName : AbstractStructure
{
        public ImageImportByName(IRawFile peFile, uint offset)
        : base(peFile, offset)
    {
    }

        public ushort Hint
    {
        get => PeFile.ReadUShort(Offset);
        set => PeFile.WriteUShort(Offset, value);
    }

        public string Name
    {
        get => PeFile.ReadAsciiString(Offset + 0x2);
        set
        {
            var source = Encoding.ASCII.GetBytes(value);
            var dest = new byte[source.Length + 1];
            Array.Copy(source, dest, source.Length);
            PeFile.WriteBytes(Offset + 0x2, dest);
        }
    }
}
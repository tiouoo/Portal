using System;
using PeNet.FileParser;

namespace PeNet.Header.Resource;

public class CvInfoPdb70 : AbstractStructure
{
    public CvInfoPdb70(IRawFile peFile, uint offset)
        : base(peFile, offset)
    {
    }

        public uint CvSignature
    {
        get => PeFile.ReadUInt(Offset);
        set => PeFile.WriteUInt(Offset, value);
    }

        public Guid Signature
    {
#if NET48 || NETSTANDARD2_0
            get => new Guid(PeFile.AsSpan(Offset + 4, 16).ToArray());
#else
        get => new(PeFile.AsSpan(Offset + 4, 16));
#endif
        set => PeFile.WriteBytes(Offset + 4, value.ToByteArray());
    }

        public uint Age
    {
        get => PeFile.ReadUInt(Offset + 0x14);
        set => PeFile.WriteUInt(Offset + 0x14, value);
    }

        public string PdbFileName => PeFile.ReadAsciiString(Offset + 0x18);
}
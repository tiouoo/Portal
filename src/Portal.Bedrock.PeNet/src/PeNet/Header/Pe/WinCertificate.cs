using System;
using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class WinCertificate : AbstractStructure
{
        public WinCertificate(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

        public uint DwLength
    {
        get => PeFile.ReadUInt(Offset);
        set => PeFile.WriteUInt(Offset, value);
    }

        public ushort WRevision
    {
        get => PeFile.ReadUShort(Offset + 0x4);
        set => PeFile.WriteUShort(Offset + 0x4, value);
    }

        public WinCertificateType WCertificateType
    {
        get => (WinCertificateType)PeFile.ReadUShort(Offset + 0x6);
        set => PeFile.WriteUShort(Offset + 0x6, (ushort)value);
    }

        public Span<byte> BCertificate
    {
        get
        {
            if (Offset + 0x8 > PeFile.Length || Offset + DwLength > PeFile.Length)
                throw new IndexOutOfRangeException("BCertificate not in PE file range.");
            return PeFile.AsSpan(Offset + 0x8, DwLength - 8);
        }
    }
}

[Flags]
public enum WinCertificateType : ushort
{
        X509 = 0x0001,

        PkcsSignedData = 0x0002,

        Reserved1 = 0x0003,

        Pkcs1Sign = 0x0009
}
using System;

namespace PeNet.FileParser;

public interface IRawFile : IDisposable
{
    long Length { get; }

    byte ReadByte(long offset);

    ushort ReadUShort(long offset);

    uint ReadUInt(long offset);

    ulong ReadULong(long offset);

    void WriteByte(long offset, byte value);

    void WriteUShort(long offset, ushort value);

    void WriteUInt(long offset, uint value);

    void WriteULong(long offset, ulong value);

    string ReadAsciiString(long offset);

    Span<byte> AsSpan(long offset, long length);

    void WriteBytes(long offset, Span<byte> bytes);

    int AppendBytes(Span<byte> bytes);

    virtual void Flush()
    {
    }
}

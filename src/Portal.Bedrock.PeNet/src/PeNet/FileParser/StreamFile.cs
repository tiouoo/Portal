using System;
using System.IO;
using System.Text;

namespace PeNet.FileParser;

public class StreamFile : IRawFile, IDisposable
{
    private const int MaxStackAlloc = 1024;
    private Stream _stream;

    public StreamFile(Stream file)
    {
        _stream = file;
    }

    public long Length => _stream.Length;

    public string ReadAsciiString(long offset)
    {
        static int GetCStringLength(Stream stream, int stringOffset)
        {
            if (stringOffset < stream.Length)
            {
                stream.Seek(stringOffset, SeekOrigin.Begin);
                var currentLength = 0;
                while (stream.ReadByte() != 0x00)
                {
                    currentLength++;
                    if (currentLength + stringOffset >= stream.Length) return 0;
                }

                return currentLength;
            }

            return 0;
        }

        var length = GetCStringLength(_stream, (int)offset);

        var tmp = length > MaxStackAlloc
            ? new byte[length]
            : stackalloc byte[length];

        _stream.Seek(offset, SeekOrigin.Begin);
        _stream.ReadExactly(tmp);
        return Encoding.ASCII.GetString(tmp);
    }

    public Span<byte> AsSpan(long offset, long length)
    {
        Span<byte> s = new byte[(int)length];
        _stream.Seek(offset, SeekOrigin.Begin);
        _stream.ReadExactly(s);
        return s;
    }

    public byte ReadByte(long offset)
    {
        _stream.Seek(offset, SeekOrigin.Begin);
        return (byte)_stream.ReadByte();
    }

    public uint ReadUInt(long offset)
    {
        Span<byte> s = stackalloc byte[4];
        _stream.Seek(offset, SeekOrigin.Begin);
        _stream.ReadExactly(s);
        return BitConverter.ToUInt32(s);
    }

    public ulong ReadULong(long offset)
    {
        Span<byte> s = stackalloc byte[8];
        _stream.Seek(offset, SeekOrigin.Begin);
        _stream.ReadExactly(s);
        return BitConverter.ToUInt64(s);
    }

    public ushort ReadUShort(long offset)
    {
        Span<byte> s = stackalloc byte[2];
        _stream.Seek(offset, SeekOrigin.Begin);
        _stream.ReadExactly(s);
        return BitConverter.ToUInt16(s);
    }

    public void WriteByte(long offset, byte value)
    {
        _stream.Seek(offset, SeekOrigin.Begin);
        _stream.WriteByte(value);
    }

    public void WriteBytes(long offset, Span<byte> bytes)
    {
        _stream.Seek(offset, SeekOrigin.Begin);
        _stream.Write(bytes);
    }

    public void WriteUInt(long offset, uint value)
    {
        Span<byte> s = BitConverter.GetBytes(value);
        _stream.Seek(offset, SeekOrigin.Begin);
        _stream.Write(s);
    }

    public void WriteULong(long offset, ulong value)
    {
        Span<byte> s = BitConverter.GetBytes(value);
        _stream.Seek(offset, SeekOrigin.Begin);
        _stream.Write(s);
    }

    public void WriteUShort(long offset, ushort value)
    {
        Span<byte> s = BitConverter.GetBytes(value);
        _stream.Seek(offset, SeekOrigin.Begin);
        _stream.Write(s);
    }

    public void Dispose()
    {
        _stream.Dispose();
    }

    public void Flush()
    {
        _stream.Flush();
    }

    public int AppendBytes(Span<byte> bytes)
    {
        if (!_stream.CanWrite)
            throw new NotSupportedException("The stream does not support writing.");

        _stream.Seek(0, SeekOrigin.End);
        var appendPosition = (int)_stream.Position;

        _stream.Write(bytes);

        return appendPosition;
    }
}

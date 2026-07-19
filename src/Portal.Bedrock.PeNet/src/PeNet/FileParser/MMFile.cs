using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace PeNet.FileParser;

/// <summary>
///     Parse the PE file as a memory mapped file.
///     This is useful for large files.
/// </summary>
public unsafe class MMFile : IRawFile, IDisposable
{
    private const int DefaultBufferSize = 65536;
    private const int MaxStackAlloc = 1024;
    private readonly MemoryStream _appendBuffer;
    private readonly object _bufferLock = new();
    private readonly string _filePath;
    private long _logicalLength;
    private MemoryMappedFile _mmf;
    private long _physicalLength;
    private MemoryMappedViewAccessor _va;
    private byte* ptr;

    public MMFile(string file)
    {
        _filePath = file;
        var fileInfo = new FileInfo(file);
        _physicalLength = fileInfo.Length;
        _logicalLength = _physicalLength;

        _mmf = MemoryMappedFile.CreateFromFile(file, FileMode.Open);
        _va = _mmf.CreateViewAccessor();
        _va.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);

        _appendBuffer = new MemoryStream(DefaultBufferSize);
        Length = _logicalLength;
    }

    public long Length { private set; get; }

    public int AppendBytes(Span<byte> bytes)
    {
        lock (_bufferLock)
        {
            var position = (int)_appendBuffer.Position;

            _appendBuffer.Write(bytes);


            _logicalLength += bytes.Length;
            Length = _logicalLength;

            if (_appendBuffer.Length >= DefaultBufferSize) FlushBuffer();

            return (int)(_physicalLength + position);
        }
    }


    public Span<byte> AsSpan(long offset, long length)
    {
        return new Span<byte>(ptr + offset, (int)length);
    }

    public void Dispose()
    {
        _va.SafeMemoryMappedViewHandle.ReleasePointer();
        _va.Dispose();
        _mmf.Dispose();
        GC.SuppressFinalize(this);
    }

    public string ReadAsciiString(long offset)
    {
        static int GetCStringLength(MemoryMappedViewAccessor va, long stringOffset)
        {
            var currentOffset = stringOffset;
            var currentLength = 0;
            while (va.ReadByte(currentOffset) != 0x00)
            {
                currentLength++;
                currentOffset++;
            }

            return currentLength;
        }

        var length = GetCStringLength(_va, offset);

        var tmp = length > MaxStackAlloc
            ? new char[length]
            : stackalloc char[length];

        for (var i = 0; i < length; i++) tmp[i] = (char)_va.ReadByte(offset + i);
#if NET48 || NETSTANDARD2_0
            fixed (char* ptr = tmp)
            {
                return new string(ptr);
            }
#else
        return new string(tmp);
#endif
    }

    public void Flush()
    {
        FlushBuffer();
    }

    public byte ReadByte(long offset)
    {
        return _va.ReadByte(offset);
    }


    public uint ReadUInt(long offset)
    {
        return _va.ReadUInt32(offset);
    }

    public ulong ReadULong(long offset)
    {
        return _va.ReadUInt64(offset);
    }

    public string ReadUnicodeString(long offset)
    {
        var size = 1;
        for (var i = offset; i < Length - 1; i++)
        {
            if (_va.ReadByte(i) == 0 && _va.ReadByte(i + 1) == 0) break;
            size++;
        }

        var bytes = new byte[size];

        _va.ReadArray(offset, bytes, 0, size);
        return Encoding.Unicode.GetString(bytes);
    }

    public string ReadUnicodeString(long offset, long length)
    {
        var size = (int)length * 2;
        var bytes = new byte[size];

        _va.ReadArray(offset, bytes, 0, size);
        return Encoding.Unicode.GetString(bytes);
    }

    public ushort ReadUShort(long offset)
    {
        return _va.ReadUInt16(offset);
    }

    public void RemoveRange(long offset, long length)
    {
        throw new NotImplementedException("RemoveRange is not available for memory mapped files");
    }

    public byte[] ToArray()
    {
        var array = new byte[Length];
        _va.ReadArray(0, array, 0, (int)Length);

        return array;
    }

    public void WriteByte(long offset, byte value)
    {
        var tmp = new[] { value };
        _va.WriteArray(offset, tmp, 0, 1);
    }

    public void WriteBytes(long offset, Span<byte> bytes)
    {
        _va.WriteArray(offset, bytes.ToArray(), 0, bytes.Length);
    }

    public void WriteUInt(long offset, uint value)
    {
        var tmp = BitConverter.GetBytes(value);
        _va.WriteArray(offset, tmp, 0, 4);
    }

    public void WriteULong(long offset, ulong value)
    {
        var tmp = BitConverter.GetBytes(value);
        _va.WriteArray(offset, tmp, 0, 8);
    }

    public void WriteUShort(long offset, ushort value)
    {
        var tmp = BitConverter.GetBytes(value);
        _va.WriteArray(offset, tmp, 0, 2);
    }

    public void FlushBuffer()
    {
        lock (_bufferLock)
        {
            if (_appendBuffer.Length == 0)
                return;

            var bufferData = _appendBuffer.ToArray();
            _appendBuffer.SetLength(0);


            var newPhysicalLength = _physicalLength + bufferData.Length;


            _va.SafeMemoryMappedViewHandle.ReleasePointer();
            _va.Dispose();
            _mmf.Dispose();


            using (var fileStream = new FileStream(_filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                fileStream.Seek(_physicalLength, SeekOrigin.Begin);
                fileStream.Write(bufferData, 0, bufferData.Length);
                fileStream.SetLength(newPhysicalLength);
            }


            _physicalLength = newPhysicalLength;

            _mmf = MemoryMappedFile.CreateFromFile(_filePath, FileMode.Open);
            _va = _mmf.CreateViewAccessor();
            _va.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        }
    }
}
using System;
using System.Collections.Generic;
using PeNet.FileParser;

namespace PeNet.Header.Net;

public class MetaDataHdr : AbstractStructure
{
    private MetaDataStreamHdr[]? _metaDataStreamsHdrs;
    private bool _metaDataStreamsHdrsParsed;
    private string? _versionString;
    private bool _versionStringParsed;

        public MetaDataHdr(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

        public uint Signature
    {
        get => PeFile.ReadUInt(Offset);
        set => PeFile.WriteUInt(Offset, value);
    }

        public ushort MajorVersion
    {
        get => PeFile.ReadUShort(Offset + 0x4);
        set => PeFile.WriteUShort(Offset + 0x4, value);
    }

        public ushort MinorVersion
    {
        get => PeFile.ReadUShort(Offset + 0x6);
        set => PeFile.WriteUShort(Offset + 0x6, value);
    }

        public uint Reserved
    {
        get => PeFile.ReadUInt(Offset + 0x8);
        set => PeFile.WriteUInt(Offset + 0x8, value);
    }

        public uint VersionLength
    {
        get => PeFile.ReadUInt(Offset + 0xC);
        set => PeFile.WriteUInt(Offset + 0xC, value);
    }

        public string? Version
    {
        get
        {
            if (!_versionStringParsed)
            {
                _versionStringParsed = true;
                try
                {
                    _versionString = PeFile.ReadAsciiString(Offset + 0x10);
                }
                catch (Exception)
                {
                    _versionString = null;
                }
            }

            return _versionString;
        }
    }


        public ushort Flags
    {
        get => PeFile.ReadUShort(VersionLength + Offset + 0x10);
        set => PeFile.WriteUShort(VersionLength + Offset + 0x10, value);
    }

        public ushort Streams
    {
        get => PeFile.ReadUShort(VersionLength + Offset + 0x12);
        set => PeFile.WriteUShort(VersionLength + Offset + 0x12, value);
    }

        public MetaDataStreamHdr[]? MetaDataStreamsHdrs
    {
        get
        {
            if (!_metaDataStreamsHdrsParsed)
            {
                _metaDataStreamsHdrsParsed = true;
                try
                {
                    _metaDataStreamsHdrs = ParseMetaDataStreamHdrs(VersionLength + Offset + 0x14);
                }
                catch (Exception)
                {
                    _metaDataStreamsHdrs = null;
                }
            }

            return _metaDataStreamsHdrs;
        }
    }

    private MetaDataStreamHdr[] ParseMetaDataStreamHdrs(long offset)
    {
        var metaDataStreamHdrs = new List<MetaDataStreamHdr>();
        var tmpOffset = offset;

        for (var i = 0; i < Streams; i++)
        {
            var metaDataStreamHdr = new MetaDataStreamHdr(PeFile, tmpOffset);
            metaDataStreamHdrs.Add(metaDataStreamHdr);
            tmpOffset += metaDataStreamHdr.HeaderLength;
        }

        return metaDataStreamHdrs.ToArray();
    }
}
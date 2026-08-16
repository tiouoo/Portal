using System;
using System.Collections.Generic;
using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageCor20Header : AbstractStructure
{
    private ImageDataDirectory? _codeManagerTable;
    private ImageDataDirectory? _exportAddressTableJumps;
    private ImageDataDirectory? _managedNativeHeader;
    private ImageDataDirectory? _metaData;
    private ImageDataDirectory? _resources;
    private ImageDataDirectory? _strongSignatureNames;
    private ImageDataDirectory? _vTableFixups;

        public ImageCor20Header(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
    }

        public uint Cb
    {
        get => PeFile.ReadUInt(Offset);
        set => PeFile.WriteUInt(Offset, value);
    }

        public ushort MajorRuntimeVersion
    {
        get => PeFile.ReadUShort(Offset + 0x4);
        set => PeFile.WriteUShort(Offset + 0x4, value);
    }

        public ushort MinorRuntimeVersion
    {
        get => PeFile.ReadUShort(Offset + 0x6);
        set => PeFile.WriteUShort(Offset + 0x6, value);
    }

        public ImageDataDirectory? MetaData
    {
        get
        {
            if (_metaData != null)
                return _metaData;

            _metaData = SetImageDataDirectory(PeFile, Offset + 0x8);
            return _metaData;
        }
    }

        public ComFlagsType Flags
        => (ComFlagsType)PeFile.ReadUInt(Offset + 0x10);

        public List<string> FlagsResolved
        => ResolveComFlags(Flags);

        public uint EntryPointToken
    {
        get => PeFile.ReadUInt(Offset + 0x14);
        set => PeFile.WriteUInt(Offset + 0x14, value);
    }

        public uint EntryPointRva
    {
        get => EntryPointToken;
        set => EntryPointToken = value;
    }

        public ImageDataDirectory? Resources
    {
        get
        {
            _resources ??= SetImageDataDirectory(PeFile, Offset + 0x18);
            return _resources;
        }
    }

        public ImageDataDirectory? StrongNameSignature
    {
        get
        {
            _strongSignatureNames ??= SetImageDataDirectory(PeFile, Offset + 0x20);
            return _strongSignatureNames;
        }
    }

        public ImageDataDirectory? CodeManagerTable
    {
        get
        {
            _codeManagerTable ??= SetImageDataDirectory(PeFile, Offset + 0x28);
            return _codeManagerTable;
        }
    }

        public ImageDataDirectory? VTableFixups
    {
        get
        {
            _vTableFixups ??= SetImageDataDirectory(PeFile, Offset + 0x30);
            return _vTableFixups;
        }
    }

        public ImageDataDirectory? ExportAddressTableJumps
    {
        get
        {
            _exportAddressTableJumps ??= SetImageDataDirectory(PeFile, Offset + 0x38);
            return _exportAddressTableJumps;
        }
    }

        public ImageDataDirectory? ManagedNativeHeader
    {
        get
        {
            _managedNativeHeader ??= SetImageDataDirectory(PeFile, Offset + 0x40);
            return _managedNativeHeader;
        }
    }

    private ImageDataDirectory? SetImageDataDirectory(IRawFile peFile, long offset)
    {
        try
        {
            return new ImageDataDirectory(peFile, offset);
        }
        catch (Exception)
        {
            return null;
        }
    }

        public static List<string> ResolveComFlags(ComFlagsType comFlags)
    {
        var st = new List<string>();
#if NET6_0_OR_GREATER
        var values = Enum.GetValues<ComFlagsType>();
#else
        var values = (ComFlagsType[])Enum.GetValues(typeof(ComFlagsType));
#endif
        foreach (var flag in values)
            if ((comFlags & flag) == flag)
                st.Add(flag.ToString());

        return st;
    }
}

[Flags]
public enum ComFlagsType : uint
{
        IlOnly = 0x00000001,

        BitRequired32 = 0x00000002,

        IlLibrary = 0x00000004,

        StrongNameSigned = 0x00000008,

        NativeEntrypoint = 0x00000010,

        TrackDebugData = 0x00010000
}
using System;
using System.Runtime.InteropServices;
using PeNet.FileParser;

namespace PeNet.Header.Pe;

public class ImageLoadConfigDirectory : AbstractStructure, IDisposable
{
    private readonly bool _is64Bit;
    private readonly IntPtr _ptr;

        public ImageLoadConfigDirectory(IRawFile peFile, long offset, bool is64Bit)
        : base(peFile, offset)
    {
        _is64Bit = is64Bit;
        var size = PeFile.ReadUInt(offset);
        var data = PeFile.ToArray();
        if (size > data.Length)
                        size = (uint)data.Length;
                _ptr = Marshal.AllocHGlobal((int)size);
        if (_ptr != IntPtr.Zero)
            if (offset + size < data.Length)
                Marshal.Copy(data, (int)offset, _ptr, (int)size);
    }

    public IMAGE_LOAD_CONFIG_DIRECTORY64 LoadConfig64 => Marshal.PtrToStructure<IMAGE_LOAD_CONFIG_DIRECTORY64>(_ptr);

    public IMAGE_LOAD_CONFIG_DIRECTORY32 LoadConfig => Marshal.PtrToStructure<IMAGE_LOAD_CONFIG_DIRECTORY32>(_ptr);

        public IRawFile PePtr => PeFile;

        public uint Size
    {
        get => PeFile.ReadUInt(Offset);
        set => PeFile.WriteUInt(Offset, value);
    }

        public uint TimeDateStamp
    {
        get => PeFile.ReadUInt(Offset + 0x4);
        set => PeFile.WriteUInt(Offset + 0x4, value);
    }

        public ushort MajorVesion
    {
        get => PeFile.ReadUShort(Offset + 0x8);
        set => PeFile.WriteUShort(Offset + 0x8, value);
    }

        public ushort MinorVersion
    {
        get => PeFile.ReadUShort(Offset + 0xA);
        set => PeFile.WriteUShort(Offset + 0xA, value);
    }

        public uint GlobalFlagsClear
    {
        get => PeFile.ReadUInt(Offset + 0xC);
        set => PeFile.WriteUInt(Offset + 0xC, value);
    }

        public uint GlobalFlagsSet
    {
        get => PeFile.ReadUInt(Offset + 0x10);
        set => PeFile.WriteUInt(Offset + 0x10, value);
    }

        public uint CriticalSectionDefaultTimeout
    {
        get => PeFile.ReadUInt(Offset + 0x14);
        set => PeFile.WriteUInt(Offset + 0x14, value);
    }

        public ulong DeCommitFreeBlockThreshold
    {
        get => _is64Bit ? PeFile.ReadULong(Offset + 0x18) : PeFile.ReadUInt(Offset + 0x18);
        set
        {
            if (_is64Bit)
                PeFile.WriteULong(Offset + 0x18, value);
            else
                PeFile.WriteUInt(Offset + 0x18, (uint)value);
        }
    }

        public ulong DeCommitTotalFreeThreshold
    {
        get => _is64Bit ? PeFile.ReadULong(Offset + 0x20) : PeFile.ReadUInt(Offset + 0x1c);
        set
        {
            if (_is64Bit)
                PeFile.WriteULong(Offset + 0x20, value);
            else
                PeFile.WriteUInt(Offset + 0x1C, (uint)value);
        }
    }

        public ulong LockPrefixTable
    {
        get => _is64Bit ? PeFile.ReadULong(Offset + 0x28) : PeFile.ReadUInt(Offset + 0x20);
        set
        {
            if (_is64Bit)
                PeFile.WriteULong(Offset + 0x28, value);
            else
                PeFile.WriteUInt(Offset + 0x20, (uint)value);
        }
    }

        public ulong MaximumAllocationSize
    {
        get => _is64Bit ? PeFile.ReadULong(Offset + 0x30) : PeFile.ReadUInt(Offset + 0x24);
        set
        {
            if (_is64Bit)
                PeFile.WriteULong(Offset + 0x30, value);
            else
                PeFile.WriteUInt(Offset + 0x24, (uint)value);
        }
    }

        public ulong VirtualMemoryThreshold
    {
        get => _is64Bit ? PeFile.ReadULong(Offset + 0x38) : PeFile.ReadUInt(Offset + 0x28);
        set
        {
            if (_is64Bit)
                PeFile.WriteULong(Offset + 0x38, value);
            else
                PeFile.WriteUInt(Offset + 0x28, (uint)value);
        }
    }

        public ulong ProcessAffinityMask
    {
        get => _is64Bit ? PeFile.ReadULong(Offset + 0x40) : PeFile.ReadUInt(Offset + 0x30);
        set
        {
            if (_is64Bit)
                PeFile.WriteULong(Offset + 0x40, value);
            else
                PeFile.WriteUInt(Offset + 0x30, (uint)value);
        }
    }

        public uint ProcessHeapFlags
    {
        get => _is64Bit ? PeFile.ReadUInt(Offset + 0x48) : PeFile.ReadUInt(Offset + 0x2C);
        set
        {
            if (_is64Bit)
                PeFile.WriteUInt(Offset + 0x48, value);
            else
                PeFile.WriteUInt(Offset + 0x2C, value);
        }
    }

        public ushort CSDVersion
    {
        get => _is64Bit ? PeFile.ReadUShort(Offset + 0x4C) : PeFile.ReadUShort(Offset + 0x34);
        set
        {
            if (_is64Bit)
                PeFile.WriteUShort(Offset + 0x4C, value);
            else
                PeFile.WriteUShort(Offset + 0x34, value);
        }
    }

        public ushort Reserved1
    {
        get => _is64Bit ? PeFile.ReadUShort(Offset + 0x4E) : PeFile.ReadUShort(Offset + 0x36);
        set
        {
            if (_is64Bit)
                PeFile.WriteUShort(Offset + 0x4E, value);
            else
                PeFile.WriteUShort(Offset + 0x36, value);
        }
    }

        public ulong EditList
    {
        get => _is64Bit ? PeFile.ReadULong(Offset + 0x50) : PeFile.ReadUInt(Offset + 0x38);
        set
        {
            if (_is64Bit)
                PeFile.WriteULong(Offset + 0x50, value);
            else
                PeFile.WriteUInt(Offset + 0x38, (uint)value);
        }
    }

        public ulong SecurityCoockie
    {
        get => _is64Bit ? PeFile.ReadULong(Offset + 0x58) : PeFile.ReadUInt(Offset + 0x3C);
        set
        {
            if (_is64Bit)
                PeFile.WriteULong(Offset + 0x58, value);
            else
                PeFile.WriteUInt(Offset + 0x3C, (uint)value);
        }
    }

        public ulong SEHandlerTable
    {
        get => _is64Bit ? PeFile.ReadULong(Offset + 0x60) : PeFile.ReadUInt(Offset + 0x40);
        set
        {
            if (_is64Bit)
                PeFile.WriteULong(Offset + 0x60, value);
            else
                PeFile.WriteUInt(Offset + 0x40, (uint)value);
        }
    }

        public ulong SEHandlerCount
    {
        get => _is64Bit ? PeFile.ReadULong(Offset + 0x68) : PeFile.ReadUInt(Offset + 0x44);
        set
        {
            if (_is64Bit)
                PeFile.WriteULong(Offset + 0x68, value);
            else
                PeFile.WriteUInt(Offset + 0x44, (uint)value);
        }
    }

        public ulong GuardCFCheckFunctionPointer
    {
        get => _is64Bit ? PeFile.ReadULong(Offset + 0x70) : PeFile.ReadUInt(Offset + 0x48);
        set
        {
            if (_is64Bit)
                PeFile.WriteULong(Offset + 0x70, value);
            else
                PeFile.WriteUInt(Offset + 0x4C, (uint)value);
        }
    }

        public ulong Reserved2
    {
        get => _is64Bit ? PeFile.ReadULong(Offset + 0x78) : PeFile.ReadUInt(Offset + 0x4C);
        set
        {
            if (_is64Bit)
                PeFile.WriteULong(Offset + 0x78, value);
            else
                PeFile.WriteUInt(Offset + 0x4C, (uint)value);
        }
    }

        public ulong GuardCFFunctionTable
    {
        get => _is64Bit ? PeFile.ReadULong(Offset + 0x80) : PeFile.ReadUInt(Offset + 0x50);
        set
        {
            if (_is64Bit)
                PeFile.WriteULong(Offset + 0x80, value);
            else
                PeFile.WriteUInt(Offset + 0x50, (uint)value);
        }
    }

        public ulong GuardCFFunctionCount
    {
        get => _is64Bit ? PeFile.ReadULong(Offset + 0x88) : PeFile.ReadUInt(Offset + 0x54);
        set
        {
            if (_is64Bit)
                PeFile.WriteULong(Offset + 0x88, value);
            else
                PeFile.WriteUInt(Offset + 0x54, (uint)value);
        }
    }

        public uint GuardFlags
    {
        get => _is64Bit ? PeFile.ReadUInt(Offset + 0x90) : PeFile.ReadUInt(Offset + 0x58);
        set
        {
            if (_is64Bit)
                PeFile.WriteUInt(Offset + 0x90, value);
            else
                PeFile.WriteUInt(Offset + 0x58, value);
        }
    }

        public IMAGE_LOAD_CONFIG_CODE_INTEGRITY CodeIntegrity =>
        _is64Bit ? LoadConfig64.CodeIntegrity : LoadConfig.CodeIntegrity;

    public void Dispose()
    {
        if (_ptr != IntPtr.Zero) Marshal.FreeHGlobal(_ptr);
    }

    ~ImageLoadConfigDirectory()
    {
        Dispose();
        GC.SuppressFinalize(this);
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct IMAGE_LOAD_CONFIG_CODE_INTEGRITY
{
        public ushort Flags;

        public ushort Catalog;

        public uint CatalogOffset;

        public uint Reserved;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
public struct IMAGE_LOAD_CONFIG_DIRECTORY64
{
        public uint Size;

        public uint TimeDateStamp;

        public ushort MajorVersion;

        public ushort MinorVersion;

        public uint GlobalFlagsClear;

        public uint GlobalFlagsSet;

        public uint CriticalSectionDefaultTimeout;

        public ulong DeCommitFreeBlockThreshold;

        public ulong DeCommitTotalFreeThreshold;

        public ulong LockPrefixTable;

        public ulong MaximumAllocationSize;

        public ulong VirtualMemoryThreshold;

        public ulong ProcessAffinityMask;

        public uint ProcessHeapFlags;

        public ushort CSDVersion;

        public ushort DependentLoadFlags;

        public ulong EditList;

        public ulong SecurityCookie;

        public ulong SEHandlerTable;

        public ulong SEHandlerCount;

        public ulong GuardCFCheckFunctionPointer;

        public ulong GuardCFDispatchFunctionPointer;

        public ulong GuardCFFunctionTable;

        public ulong GuardCFFunctionCount;

        public uint GuardFlags;

        public IMAGE_LOAD_CONFIG_CODE_INTEGRITY CodeIntegrity;

        public ulong GuardAddressTakenIatEntryTable;

        public ulong GuardAddressTakenIatEntryCount;

        public ulong GuardLongJumpTargetTable;

        public ulong GuardLongJumpTargetCount;

        public ulong DynamicValueRelocTable;

        public ulong CHPEMetadataPointer;

        public ulong GuardRFFailureRoutine;

        public ulong GuardRFFailureRoutineFunctionPointer;

        public uint DynamicValueRelocTableOffset;

        public ushort DynamicValueRelocTableSection;

        public ushort Reserved2;

        public ulong GuardRFVerifyStackPointerFunctionPointer;

        public uint HotPatchTableOffset;

        public uint Reserved3;

        public ulong EnclaveConfigurationPointer;

        public ulong VolatileMetadataPointer;

        public ulong GuardEHContinuationTable;

        public ulong GuardEHContinuationCount;

        public ulong GuardXFGCheckFunctionPointer; 

        public ulong GuardXFGDispatchFunctionPointer; 

        public ulong GuardXFGTableDispatchFunctionPointer; 

        public ulong CastGuardOsDeterminedFailureMode; 

        public ulong GuardMemcpyFunctionPointer; 
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
public struct IMAGE_LOAD_CONFIG_DIRECTORY32
{
        public uint Size;

        public uint TimeDateStamp;

        public ushort MajorVersion;

        public ushort MinorVersion;

        public uint GlobalFlagsClear;

        public uint GlobalFlagsSet;

        public uint CriticalSectionDefaultTimeout;

        public uint DeCommitFreeBlockThreshold;

        public uint DeCommitTotalFreeThreshold;

        public uint LockPrefixTable;

        public uint MaximumAllocationSize;

        public uint VirtualMemoryThreshold;

        public uint ProcessHeapFlags;

        public uint ProcessAffinityMask;

        public ushort CSDVersion;

        public ushort DependentLoadFlags;

        public uint EditList;

        public uint SecurityCookie;

        public uint SEHandlerTable;

        public uint SEHandlerCount;

        public uint GuardCFCheckFunctionPointer;

        public uint GuardCFDispatchFunctionPointer;

        public uint GuardCFFunctionTable;

        public uint GuardCFFunctionCount;

        public uint GuardFlags;

        public IMAGE_LOAD_CONFIG_CODE_INTEGRITY CodeIntegrity;

        public uint GuardAddressTakenIatEntryTable;

        public uint GuardAddressTakenIatEntryCount;

        public uint GuardLongJumpTargetTable;

        public uint GuardLongJumpTargetCount;

        public uint DynamicValueRelocTable;

        public uint CHPEMetadataPointer;

        public uint GuardRFFailureRoutine;

        public uint GuardRFFailureRoutineFunctionPointer;

        public uint DynamicValueRelocTableOffset;

        public ushort DynamicValueRelocTableSection;

        public ushort Reserved2;

        public uint GuardRFVerifyStackPointerFunctionPointer;

        public uint HotPatchTableOffset;

        public uint Reserved3;

        public uint EnclaveConfigurationPointer;

        public uint VolatileMetadataPointer;

        public uint GuardEHContinuationTable;

        public uint GuardEHContinuationCount;

        public uint GuardXFGCheckFunctionPointer; 

        public uint GuardXFGDispatchFunctionPointer; 

        public uint GuardXFGTableDispatchFunctionPointer; 

        public uint CastGuardOsDeterminedFailureMode; 

        public uint GuardMemcpyFunctionPointer; 
}
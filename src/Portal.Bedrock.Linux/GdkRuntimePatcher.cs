using System.Buffers.Binary;

namespace Portal.Bedrock.Linux;

/// <summary>
/// 对 Portal 管理的 GDK-Proton 运行时应用兼容性补丁。
/// Wine 的 combase.dll 将 RoOriginateErrorW 导出为未实现的桩函数，游戏（GDK 构建）
/// 调用它报告 WinRT 错误时 Wine 会直接 abort 进程。补丁把该导出改写为无害的
/// xor eax, eax; ret; nop，让调用直接返回成功，避免进程被终止。
/// </summary>
internal static class GdkRuntimePatcher
{
    private static readonly byte[] NoOp = [0x31, 0xC0, 0xC3, 0x90];

    /// <summary>
    /// 若目标运行时中 RoOriginateErrorW 尚未被修补，则将其改写为 no-op。
    /// 幂等：已修补或找不到导出时返回 false。
    /// </summary>
    public static bool PatchCombaseRoOriginateErrorW(string protonRoot)
    {
        var combasePath = Path.Combine(protonRoot, "files", "lib", "wine", "x86_64-windows", "combase.dll");
        if (!File.Exists(combasePath)) return false;

        var bytes = File.ReadAllBytes(combasePath);
        var functionRva = FindExportRva(bytes, "RoOriginateErrorW");
        if (functionRva is not { } rva) return false;

        var fileOffset = RvaToFileOffset(bytes, rva);
        if (fileOffset is not { } offset || offset + NoOp.Length > bytes.Length) return false;

        if (bytes.AsSpan((int)offset, NoOp.Length).SequenceEqual(NoOp)) return false;

        NoOp.CopyTo(bytes, offset);
        File.WriteAllBytes(combasePath, bytes);
        return true;
    }

    private static uint? FindExportRva(byte[] pe, string exportName)
    {
        var export = ReadExportDirectory(pe);
        if (export is null) return null;
        var (numberOfNames, addressOfFunctions, addressOfNames, addressOfNameOrdinals) = export.Value;

        for (var i = 0u; i < numberOfNames; i++)
        {
            if (!TryReadUInt(pe, RvaToFileOffset(pe, addressOfNames + (uint)(i * 4)), out var nameRva)) continue;
            var nameOffset = RvaToFileOffset(pe, nameRva);
            if (nameOffset is null || !HasAsciiName(pe, nameOffset.Value, exportName)) continue;

            if (!TryReadUShort(pe, RvaToFileOffset(pe, addressOfNameOrdinals + (uint)(i * 2)), out var ordinal)) return null;
            if (!TryReadUInt(pe, RvaToFileOffset(pe, addressOfFunctions + (uint)(ordinal * 4)), out var functionRva))
                return null;
            return functionRva;
        }

        return null;
    }

    private static (uint NumberOfNames, uint AddressOfFunctions, uint AddressOfNames, uint AddressOfNameOrdinals)?
        ReadExportDirectory(byte[] pe)
    {
        if (!TryReadUInt(pe, 0x3C, out var peOffset) || peOffset + 0x18 + 0x70 > pe.Length) return null;
        if (pe[peOffset] != (byte)'P' || pe[peOffset + 1] != (byte)'E') return null;

        var optionalStart = peOffset + 0x18;
        if (!TryReadUShort(pe, optionalStart, out var magic)) return null;
        int dataDirectoryStart;
        if (magic == 0x20B)
            dataDirectoryStart = (int)optionalStart + 112;
        else if (magic == 0x10B)
            dataDirectoryStart = (int)optionalStart + 96;
        else
            return null;

        if (!TryReadUInt(pe, (uint)dataDirectoryStart, out var exportDirectoryRva)) return null;
        var offset = RvaToFileOffset(pe, exportDirectoryRva);
        if (offset is null || offset.Value + 40 > pe.Length) return null;

        return (
            ReadUInt(pe, offset.Value + 24),
            ReadUInt(pe, offset.Value + 28),
            ReadUInt(pe, offset.Value + 32),
            ReadUInt(pe, offset.Value + 36));
    }

    private static uint? RvaToFileOffset(byte[] pe, uint rva)
    {
        if (rva == 0 || !TryReadUInt(pe, 0x3C, out var peOffset)) return null;
        if (!TryReadUShort(pe, peOffset + 6, out var numberOfSections)) return null;
        if (!TryReadUShort(pe, peOffset + 20, out var sizeOfOptionalHeader)) return null;

        var sectionTable = (uint)(peOffset + 24 + sizeOfOptionalHeader);
        for (var i = 0u; i < numberOfSections; i++)
        {
            var section = sectionTable + i * 40;
            if (section + 40 > pe.Length) return null;

            var virtualSize = ReadUInt(pe, section + 8);
            var virtualAddress = ReadUInt(pe, section + 12);
            var sizeOfRawData = ReadUInt(pe, section + 16);
            var pointerToRawData = ReadUInt(pe, section + 20);

            var sectionSize = Math.Max(virtualSize, sizeOfRawData);
            if (rva < virtualAddress || rva - virtualAddress >= sectionSize) continue;

            var offset = rva - virtualAddress + pointerToRawData;
            return offset <= pe.Length ? offset : null;
        }

        return null;
    }

    private static bool HasAsciiName(byte[] pe, uint offset, string name)
    {
        if (offset + name.Length > pe.Length) return false;
        for (var i = 0; i < name.Length; i++)
        {
            if (pe[offset + i] != (byte)name[i]) return false;
        }

        var terminator = pe[offset + name.Length];
        return terminator == 0 || offset + name.Length + 1 > pe.Length;
    }

    private static uint ReadUInt(byte[] pe, uint offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan((int)offset));

    private static bool TryReadUInt(byte[] pe, uint? offset, out uint value)
    {
        if (offset is not { } o || o + 4 > pe.Length)
        {
            value = 0;
            return false;
        }

        value = ReadUInt(pe, o);
        return true;
    }

    private static bool TryReadUShort(byte[] pe, uint? offset, out ushort value)
    {
        if (offset is not { } o || o + 2 > pe.Length)
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan((int)o));
        return true;
    }
}

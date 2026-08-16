using System;
using System.Collections.Generic;
using PeNet.FileParser;
using PeNet.Header.Pe;

namespace PeNet;

public static class ExtensionMethods
{
    public static ulong RvaToOffset(this ulong rva, ICollection<ImageSectionHeader> sectionHeaders)
    {
        static ImageSectionHeader? GetSectionForRva(ICollection<ImageSectionHeader> sh, ulong relVirAdr)
        {
            ImageSectionHeader? sec = null;
            uint secSize;

            for (var i = 0; i < sh.Count; i++)
            {
                secSize = sh.ElementAt(i).VirtualSize == 0
                    ? sh.ElementAt(i).SizeOfRawData
                    : sh.ElementAt(i).VirtualSize;

                if (relVirAdr >= sh.ElementAt(i).VirtualAddress
                    && relVirAdr < sh.ElementAt(i).VirtualAddress + secSize)
                    sec = sh.ElementAt(i);
            }

            if (sec != null)
                return sec;

            for (var i = sh.Count - 1; i >= 0; i--)
            {
                secSize = sh.ElementAt(i).VirtualSize == 0
                    ? sh.ElementAt(i).SizeOfRawData
                    : sh.ElementAt(i).VirtualSize;

                if (relVirAdr >= sh.ElementAt(i).VirtualAddress &&
                    relVirAdr <= sh.ElementAt(i).VirtualAddress + secSize)
                    sec = sh.ElementAt(i);
            }

            return sec;
        }

        var section = GetSectionForRva(sectionHeaders, rva);

        if (section is null) throw new Exception("Cannot find corresponding section.");

        return rva - section.VirtualAddress + section.PointerToRawData;
    }

    public static uint RvaToOffset(this uint rva, ICollection<ImageSectionHeader> sectionHeaders)
    {
        return (uint)((ulong)rva).RvaToOffset(sectionHeaders);
    }

    public static ulong OffsetToRva(this ulong offset, ICollection<ImageSectionHeader> sectionHeaders)
    {
        static ImageSectionHeader? GetSectionForOffset(ICollection<ImageSectionHeader> sh, ulong rawOffset)
        {
            ImageSectionHeader? sec = null;
            for (var i = 0; i < sh.Count; i++)
                if (rawOffset >= sh.ElementAt(i).PointerToRawData
                    && rawOffset < sh.ElementAt(i).PointerToRawData + sh.ElementAt(i).SizeOfRawData)
                    sec = sh.ElementAt(i);

            if (sec != null)
                return sec;

            for (var i = sh.Count - 1; i >= 0; i--)
                if (rawOffset >= sh.ElementAt(i).PointerToRawData &&
                    rawOffset <= sh.ElementAt(i).PointerToRawData + sh.ElementAt(i).SizeOfRawData)
                    sec = sh.ElementAt(i);

            return sec;
        }

        var section = GetSectionForOffset(sectionHeaders, offset);

        if (section is null) throw new Exception("Cannot find corresponding section.");

        return offset + section.VirtualAddress - section.PointerToRawData;
    }

    public static uint OffsetToRva(this uint offset, ICollection<ImageSectionHeader> sectionHeaders)
    {
        return (uint)((ulong)offset).OffsetToRva(sectionHeaders);
    }

    public static bool TryRvaToOffset(this uint rva, ICollection<ImageSectionHeader>? sectionHeaders,
        out uint fileOffset)
    {
        fileOffset = 0;

        if (sectionHeaders is null)
            return false;

        try
        {
            fileOffset = rva.RvaToOffset(sectionHeaders);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool Is64Bit(this IRawFile peFile)
    {
        return peFile.ReadUShort(peFile.ReadUInt(0x3c) + 0x18) == (ushort)MagicType.Bit64;
    }

    public static bool Is32Bit(this IRawFile peFile)
    {
        return peFile.ReadUShort(peFile.ReadUInt(0x3c) + 0x18) == (ushort)MagicType.Bit32;
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using PeNet.FileParser;
using PeNet.Header.Pe;

namespace PeNet;

public static class ExtensionMethods
{
        public static ulong VaToOffset(this ulong va, ICollection<ImageSectionHeader> sectionHeaders)
    {
        var rva = va - sectionHeaders.First().ImageBaseAddress;
        return rva.RvaToOffset(sectionHeaders);
    }

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

        public static string ToHexString(this ICollection<byte> bytes)
    {
        var hex = new StringBuilder(bytes.Count * 2);
        foreach (var b in bytes)
            hex.AppendFormat("{0:x2}", b);
        return $"0x{hex}";
    }

        public static string ToHexString(this ICollection<ushort> values)
    {
        var hex = new StringBuilder(values.Count * 2);
        foreach (var b in values)
            hex.AppendFormat("{0:X4}", b);
        return $"0x{hex}";
    }


        public static string ToHexString(this byte value)
    {
        return $"0x{value:X2}";
    }

        public static string ToHexString(this ushort value)
    {
        return $"0x{value:X4}";
    }

        public static string ToHexString(this uint value)
    {
        return $"0x{value:X8}";
    }

        public static string ToHexString(this ulong value)
    {
        return $"0x{value:X16}";
    }

        public static List<string> ToHexString(this byte[] input, ulong from, ulong length)
    {
        var hexList = new List<string>();
        for (var i = from; i < from + length; i++) hexList.Add(input[i].ToString("X2"));
        return hexList;
    }

        public static long ToIntFromHexString(this string hexString)
    {
        return (long)(new Int64Converter().ConvertFromString(hexString) ?? throw new InvalidOperationException());
    }

        public static int UStringByteLength(this string s)
    {
        return s.Length * 2 + 2;
    }

        public static uint PaddingBytes(this long offset, int alignment)
    {
        return ((uint)offset).PaddingBytes(alignment);
    }

        public static uint PaddingBytes(this int offset, int alignment)
    {
        return ((uint)offset).PaddingBytes(alignment);
    }

        public static uint PaddingBytes(this uint offset, int alignment)
    {
        return offset % (uint)(alignment / 8);
    }

        public static bool Is64Bit(this IRawFile peFile)
    {
        return peFile.ReadUShort(peFile.ReadUInt(0x3c) + 0x18) == (ushort)MagicType.Bit64;
    }

        public static bool Is32Bit(this IRawFile peFile)
    {
        return peFile.ReadUShort(peFile.ReadUInt(0x3c) + 0x18) == (ushort)MagicType.Bit32;
    }

        public static IEnumerable<T> OrEmpty<T>(this IEnumerable<T>? enumerable)
    {
        return enumerable ?? Enumerable.Empty<T>();
    }

        public static IEnumerable<TOut> TrySelect<T, TOut>(this IEnumerable<T> source,
        Func<T, (bool Success, TOut Value)> tryFunc)
    {
        return source.Select(tryFunc)
            .Where(o => o.Success)
            .Select(o => o.Value);
    }

        public static byte[] LittleEndianBytes(this ushort input)
    {
        var bytes = BitConverter.GetBytes(input);
        if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return bytes;
    }

        public static byte[] LittleEndianBytes(this uint input)
    {
        var bytes = BitConverter.GetBytes(input);
        if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return bytes;
    }

        public static void WriteBytes<T>(this T[] destination, int offset, Span<T> source)
    {
        source.CopyTo(new Span<T>(destination, offset, source.Length));
    }
}
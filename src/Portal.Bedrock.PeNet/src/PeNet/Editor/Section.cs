using System;
using System.Linq;
using PeNet.Header.Pe;

namespace PeNet;

public partial class PeFile
{
    public void AddSection(string name, byte[] unalignedSize, ScnCharacteristicsType characteristics)
    {
        if (ImageNtHeaders is null)
            throw new Exception("IMAGE_NT_HEADERS must not be null.");
        if (ImageDosHeader is null)
            throw new Exception("IMAGE_DOS_HEADER must not be null");

        uint GetNewSizeOfImage()
        {
            var factor = unalignedSize.Length / (double)ImageNtHeaders.OptionalHeader.SectionAlignment;
            var additionalSize = (uint)Math.Ceiling(factor) * ImageNtHeaders!.OptionalHeader.SectionAlignment;
            return ImageNtHeaders.OptionalHeader.SizeOfImage + additionalSize;
        }

        uint GetNewSecHeaderOffset()
        {
            var sizeOfSection = 0x28;
            var x = (uint)ImageNtHeaders!.FileHeader.SizeOfOptionalHeader + 0x18;
            var startOfSectionHeader = ImageDosHeader.E_lfanew + x;
            return (uint)(startOfSectionHeader + ImageNtHeaders.FileHeader.NumberOfSections * sizeOfSection);
        }

        uint GetNewSecVa()
        {
            var lastSec = ImageSectionHeaders!.OrderByDescending(sh => sh.VirtualAddress).First();
            var vaLastSecEnd = lastSec.VirtualAddress + lastSec.VirtualSize;
            var factor = vaLastSecEnd / (double)ImageNtHeaders.OptionalHeader.SectionAlignment;
            return (uint)(Math.Ceiling(factor) * ImageNtHeaders.OptionalHeader.SectionAlignment);
        }

        uint GetNewRawSecSize()
        {
            var factor = unalignedSize.Length / (double)ImageNtHeaders!.OptionalHeader.FileAlignment;
            return (uint)(Math.Ceiling(factor) * ImageNtHeaders!.OptionalHeader.FileAlignment);
        }

        var newRawSectionSize = GetNewRawSecSize();

        RawFile.AppendBytes(unalignedSize);
        RawFile.AppendBytes(new byte[newRawSectionSize - unalignedSize.Length]);
        var paNewSec = ImageSectionHeaders!.Last().PointerToRawData + ImageSectionHeaders!.Last().SizeOfRawData;

        var newSection =
            new ImageSectionHeader(RawFile, GetNewSecHeaderOffset(), ImageNtHeaders.OptionalHeader.ImageBase)
            {
                Name = name,
                VirtualSize = newRawSectionSize,
                VirtualAddress = GetNewSecVa(),
                SizeOfRawData = newRawSectionSize,
                PointerToRawData = paNewSec,
                PointerToRelocations = 0,
                PointerToLinenumbers = 0,
                NumberOfRelocations = 0,
                NumberOfLinenumbers = 0,
                Characteristics = characteristics
            };

        ImageNtHeaders.FileHeader.NumberOfSections = (ushort)(ImageNtHeaders.FileHeader.NumberOfSections + 1);

        ImageNtHeaders.OptionalHeader.SizeOfImage = GetNewSizeOfImage();

        _nativeStructureParsers.ReparseSectionHeaders();
    }

    public void AddSection(string name, int unalignedSize, ScnCharacteristicsType characteristics)
    {
        AddSection(name, new byte[unalignedSize], characteristics);
    }
}

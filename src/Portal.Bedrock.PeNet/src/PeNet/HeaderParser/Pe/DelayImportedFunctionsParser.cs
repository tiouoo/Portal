using System.Collections.Generic;
using PeNet.FileParser;
using PeNet.Header.Pe;

namespace PeNet.HeaderParser.Pe;

internal class DelayImportedFunctionsParser : SafeParser<ImportFunction[]>
{
    private readonly ImageDataDirectory[] _dataDirectories;
    private readonly ImageDelayImportDescriptor[]? _importDescriptors;
    private readonly bool _is64Bit;
    private readonly ImageSectionHeader[] _sectionHeaders;

    internal DelayImportedFunctionsParser(
        IRawFile peFile,
        ImageDelayImportDescriptor[]? importDescriptors,
        ImageSectionHeader[] sectionHeaders,
        ImageDataDirectory[] dataDirectories,
        bool is64Bit) :
        base(peFile, 0)
    {
        _importDescriptors = importDescriptors;
        _sectionHeaders = sectionHeaders;
        _dataDirectories = dataDirectories;
        _is64Bit = is64Bit;
    }

    protected override ImportFunction[]? ParseTarget()
    {
        if (_importDescriptors == null)
            return null;

        var impFuncs = new List<ImportFunction>();
        var sizeOfThunk = (uint)(_is64Bit ? 0x8 : 0x4); 
        var ordinalBit = _is64Bit ? 0x8000000000000000 : 0x80000000;
        var ordinalMask = (ulong)(_is64Bit ? 0x7FFFFFFFFFFFFFFF : 0x7FFFFFFF);
        var iat = _dataDirectories[(int)DataDirectoryType.DelayImport];

        foreach (var idesc in _importDescriptors)
        {
            var dllAdr = idesc.SzName.RvaToOffset(_sectionHeaders);
            var dll = PeFile.ReadAsciiString(dllAdr);
            if (IsModuleNameTooLong(dll))
                continue;
            var tmpAdr = idesc.PInt;
            if (tmpAdr == 0)
                continue;

            var thunkAdr = tmpAdr.RvaToOffset(_sectionHeaders);
            uint round = 0;

            while (true)
            {
                var t = new ImageThunkData(PeFile, thunkAdr + round * sizeOfThunk, _is64Bit);
                var iatOffset = idesc.PInt + round * sizeOfThunk - iat.VirtualAddress;

                if (t.AddressOfData == 0)
                    break;

                
                
                
                

                if ((t.Ordinal & ordinalBit) == ordinalBit) 
                {
                    impFuncs.Add(new ImportFunction(null, dll, (ushort)(t.Ordinal & ordinalMask), iatOffset));
                }
                else 
                {
                    var ibn = new ImageImportByName(PeFile,
                        ((uint)t.AddressOfData).RvaToOffset(_sectionHeaders));
                    impFuncs.Add(new ImportFunction(ibn.Name, dll, ibn.Hint, iatOffset));
                }

                round++;
            }
        }

        return impFuncs.ToArray();
    }

    private bool IsModuleNameTooLong(string dllName)
    {
        return dllName.Length > 256;
    }
}
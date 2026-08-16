using System.Collections.Generic;
using System.Linq;
using PeNet.FileParser;
using PeNet.Header.Pe;

namespace PeNet.HeaderParser.Pe;

internal class DataDirectoryParsers
{
    private readonly ImageDataDirectory[] _dataDirectories;
    private readonly bool _is32Bit;
    private readonly IRawFile _peFile;
    private ImageImportDescriptorsParser? _imageImportDescriptorsParser;
    private ImportedFunctionsParser _importedFunctionsParser;
    private ImageSectionHeader[] _sectionHeaders;

    public DataDirectoryParsers(
        IRawFile peFile,
        IEnumerable<ImageDataDirectory> dataDirectories,
        IEnumerable<ImageSectionHeader> sectionHeaders,
        bool is32Bit
    )
    {
        _peFile = peFile;
        _dataDirectories = dataDirectories.ToArray();
        _sectionHeaders = sectionHeaders.ToArray();
        _is32Bit = is32Bit;

        _imageImportDescriptorsParser = InitImageImportDescriptorsParser();
        _importedFunctionsParser = InitImportedFunctionsParser();
    }

    public ImageImportDescriptor[]? ImageImportDescriptors => _imageImportDescriptorsParser?.GetParserTarget();

    public ImportFunction[]? ImportFunctions => _importedFunctionsParser.GetParserTarget();

    internal void ReparseImportDescriptors(ImageSectionHeader[] sectionHeaders)
    {
        _sectionHeaders = sectionHeaders;
        _imageImportDescriptorsParser = InitImageImportDescriptorsParser();
    }

    internal void ReparseImportedFunctions()
    {
        _importedFunctionsParser = InitImportedFunctionsParser();
    }

    private ImportedFunctionsParser InitImportedFunctionsParser()
    {
        return new ImportedFunctionsParser(
            _peFile,
            ImageImportDescriptors,
            _sectionHeaders,
            _dataDirectories,
            !_is32Bit
        );
    }

    private ImageImportDescriptorsParser? InitImageImportDescriptorsParser()
    {
        return _dataDirectories[(int)DataDirectoryType.Import].VirtualAddress
            .TryRvaToOffset(_sectionHeaders, out var offset)
            ? new ImageImportDescriptorsParser(_peFile, offset)
            : null;
    }
}

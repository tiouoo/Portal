using System;
using System.IO;
using PeNet.FileParser;
using PeNet.Header.Pe;
using PeNet.HeaderParser.Pe;

namespace PeNet;

public partial class PeFile : IDisposable
{
    private readonly DataDirectoryParsers? _dataDirectoryParsers;
    private readonly NativeStructureParsers _nativeStructureParsers;

    public PeFile(IRawFile peFile)
    {
        RawFile = peFile;

        _nativeStructureParsers = new NativeStructureParsers(RawFile);

        if (_nativeStructureParsers.ImageNtHeaders?.Signature
            != 0x4550)
            throw new Exception("Not a PE file");

        if (ImageNtHeaders?.OptionalHeader?.DataDirectory != null)
            if (ImageSectionHeaders != null)
                _dataDirectoryParsers = new DataDirectoryParsers(
                    RawFile,
                    ImageNtHeaders.OptionalHeader.DataDirectory,
                    ImageSectionHeaders,
                    Is32Bit
                );
    }

    public PeFile(Stream peFile)
        : this(new StreamFile(peFile))
    {
    }

    public IRawFile RawFile { get; }

    public bool Is64Bit => RawFile.Is64Bit();

    public bool Is32Bit => RawFile.Is32Bit();

    public ImageDosHeader? ImageDosHeader => _nativeStructureParsers.ImageDosHeader;

    public ImageNtHeaders? ImageNtHeaders => _nativeStructureParsers.ImageNtHeaders;

    public ImageSectionHeader[]? ImageSectionHeaders => _nativeStructureParsers.ImageSectionHeaders;

    public ImageImportDescriptor[]? ImageImportDescriptors => _dataDirectoryParsers?.ImageImportDescriptors;

    public ImportFunction[]? ImportedFunctions => _dataDirectoryParsers?.ImportFunctions;

    public long FileSize => RawFile.Length;

    public void Dispose()
    {
        RawFile.Dispose();
    }

    public void Flush()
    {
        RawFile.Flush();
    }
}

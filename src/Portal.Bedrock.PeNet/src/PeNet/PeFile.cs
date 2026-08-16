using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using PeNet.Crypto;
using PeNet.FileParser;
using PeNet.Header.Authenticode;
using PeNet.Header.ImpHash;
using PeNet.Header.Net;
using PeNet.Header.Pe;
using PeNet.Header.Resource;
using PeNet.HeaderParser.Authenticode;
using PeNet.HeaderParser.Net;
using PeNet.HeaderParser.Pe;

namespace PeNet;

public partial class PeFile : IDisposable
{
    private readonly AuthenticodeParser _authenticodeParser;
    private readonly DataDirectoryParsers? _dataDirectoryParsers;
    private readonly DotNetStructureParsers _dotNetStructureParsers;
    private readonly NativeStructureParsers _nativeStructureParsers;

    private string? _impHash;
    private string? _md5;
    private NetGuids? _netGuids;
    private string? _sha1;
    private string? _sha256;
    private string? _typeRefHash;


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

        _dotNetStructureParsers = new DotNetStructureParsers(
            RawFile,
            ImageComDescriptor,
            ImageSectionHeaders
        );

        _authenticodeParser = new AuthenticodeParser(this);
    }

        public PeFile(byte[] buff)
        : this(new BufferFile(buff))
    {
    }

        public PeFile(string peFile)
        : this(File.ReadAllBytes(peFile))
    {
    }

        public PeFile(Stream peFile)
        : this(new StreamFile(peFile))
    {
    }


        public IRawFile RawFile { get; }

        public bool IsDll
        => ImageNtHeaders?.FileHeader.Characteristics.HasFlag(FileCharacteristicsType.Dll) ?? false;


        public bool IsExe
        => ImageNtHeaders?.FileHeader.Characteristics.HasFlag(FileCharacteristicsType.ExecutableImage) ?? false;

        public bool IsDotNet
        => ImageComDescriptor != null;

        public bool IsDriver => ImportedFunctions != null &&
                            ImageNtHeaders?.OptionalHeader.Subsystem == SubsystemType.Native &&
                            ImportedFunctions.Any(i => i.DLL == "ntoskrnl.exe");

        public bool IsAuthenticodeSigned => SigningAuthenticodeCertificate != null;

        public bool HasValidAuthenticodeSignature => AuthenticodeInfo?.IsAuthenticodeValid ?? false;


        public bool IsTrustedAuthenticodeSignature => AuthenticodeInfo?.SigningCertificate?.Verify() ?? false;

        public AuthenticodeInfo? AuthenticodeInfo => _authenticodeParser.ParseTarget();

        public bool Is64Bit => RawFile.Is64Bit();

        public bool Is32Bit => RawFile.Is32Bit();

        public ImageDosHeader? ImageDosHeader => _nativeStructureParsers.ImageDosHeader;

        public ImageNtHeaders? ImageNtHeaders => _nativeStructureParsers.ImageNtHeaders;

        public ImageSectionHeader[]? ImageSectionHeaders => _nativeStructureParsers.ImageSectionHeaders;


        public ImageExportDirectory? ImageExportDirectory => _dataDirectoryParsers?.ImageExportDirectories;

        public ImageImportDescriptor[]? ImageImportDescriptors => _dataDirectoryParsers?.ImageImportDescriptors;

        public ImageBaseRelocation[]? ImageRelocationDirectory => _dataDirectoryParsers?.ImageBaseRelocations;

        public ImageDebugDirectory[]? ImageDebugDirectory => _dataDirectoryParsers?.ImageDebugDirectory;

        public ExportFunction[]? ExportedFunctions => _dataDirectoryParsers?.ExportFunctions;

        public ImportFunction[]? ImportedFunctions => _dataDirectoryParsers?.ImportFunctions;

        public ImportFunction[]? DelayImportedFunctions => _dataDirectoryParsers?.DelayImportFunctions;

        public ImageResourceDirectory? ImageResourceDirectory => _dataDirectoryParsers?.ImageResourceDirectory;

        public Resources? Resources => _dataDirectoryParsers?.Resources;

        public RuntimeFunction[]? ExceptionDirectory => _dataDirectoryParsers?.RuntimeFunctions;

        public WinCertificate? WinCertificate => _dataDirectoryParsers?.WinCertificate;

        public ImageBoundImportDescriptor? ImageBoundImportDescriptor =>
        _dataDirectoryParsers?.ImageBoundImportDescriptor;

        public ImageTlsDirectory? ImageTlsDirectory => _dataDirectoryParsers?.ImageTlsDirectory;

        public ImageDelayImportDescriptor[]? ImageDelayImportDescriptors =>
        _dataDirectoryParsers?.ImageDelayImportDescriptors;

        public ImageLoadConfigDirectory? ImageLoadConfigDirectory => _dataDirectoryParsers?.ImageLoadConfigDirectory;

        public ImageCor20Header? ImageComDescriptor => _dataDirectoryParsers?.ImageComDescriptor;

        public X509Certificate2? SigningAuthenticodeCertificate => AuthenticodeInfo?.SigningCertificate;

        public MetaDataHdr? MetaDataHdr => _dotNetStructureParsers.MetaDataHdr;

        public MetaDataStreamString? MetaDataStreamString => _dotNetStructureParsers.MetaDataStreamString;

        public MetaDataStreamUs? MetaDataStreamUs => _dotNetStructureParsers.MetaDataStreamUs;

        public MetaDataStreamGuid? MetaDataStreamGuid => _dotNetStructureParsers.MetaDataStreamGuid;

        public byte[]? MetaDataStreamBlob => _dotNetStructureParsers.MetaDataStreamBlob;

        public MetaDataTablesHdr? MetaDataStreamTablesHeader => _dotNetStructureParsers.MetaDataStreamTablesHeader;

        public string? Sha256
        => _sha256 ??= Hash.ComputeHash(RawFile.AsSpan(0, RawFile.Length), Algorithm.Sha256);


        public string? Sha1
        => _sha1 ??= Hash.ComputeHash(RawFile.AsSpan(0, RawFile.Length), Algorithm.Sha1);

        public string? Md5
        => _md5 ??= Hash.ComputeHash(RawFile.AsSpan(0, RawFile.Length), Algorithm.Md5);

        public string? ImpHash
        => _impHash ??= new ImportHash(ImportedFunctions)?.ImpHash;

        public string? TypeRefHash
        => _typeRefHash ??=
            IsDotNet ? new TypeRefHash(MetaDataStreamTablesHeader, MetaDataStreamString).ComputeHash() : null;

        public List<Guid>? ClrModuleVersionIds
        => (_netGuids ??= new NetGuids(this)).ModuleVersionIds;

        public Guid? ClrComTypeLibId
        => (_netGuids ??= new NetGuids(this)).ComTypeLibId;

        public long FileSize => RawFile.Length;


    public void Dispose()
    {
        RawFile.Dispose();
    }

        public static bool TryParse(string file, out PeFile? peFile)
    {
        return TryParse(File.ReadAllBytes(file), out peFile);
    }

        public static bool TryParse(byte[] buff, out PeFile? peFile)
    {
        peFile = null;

        if (!IsPeFile(buff))
            return false;

        try
        {
            peFile = new PeFile(buff);
        }
        catch
        {
            return false;
        }

        return true;
    }


        public static bool TryParse(Stream file, out PeFile? peFile)
    {
        peFile = null;

        if (!IsPeFile(file))
            return false;

        try
        {
            peFile = new PeFile(file);
        }
        catch
        {
            return false;
        }

        return true;
    }

        public static bool TryParse(MMFile file, out PeFile? peFile)
    {
        peFile = null;

        if (!IsPeFile(file))
            return false;

        try
        {
            peFile = new PeFile(file);
        }
        catch
        {
            return false;
        }

        return true;
    }

    public void Flush()
    {
        RawFile.Flush();
    }

        public bool HasValidAuthenticodeCertChain(bool useOnlineCrl)
    {
        return AuthenticodeInfo?.SigningCertificate != null
               && HasValidAuthenticodeCertChain(AuthenticodeInfo.SigningCertificate, TimeSpan.FromSeconds(10),
                   useOnlineCrl);
    }

        public static bool HasValidAuthenticodeCertChain(X509Certificate2? cert, TimeSpan urlRetrievalTimeout,
        bool useOnlineCRL = true, bool excludeRoot = true)
    {
        if (cert == null)
            return false;

        using var chain = new X509Chain
        {
            ChainPolicy =
            {
                RevocationFlag = excludeRoot ? X509RevocationFlag.ExcludeRoot : X509RevocationFlag.EntireChain,
                RevocationMode = useOnlineCRL ? X509RevocationMode.Online : X509RevocationMode.Offline,
                UrlRetrievalTimeout = urlRetrievalTimeout,
                VerificationFlags = X509VerificationFlags.NoFlag
            }
        };
        return chain.Build(cert);
    }

        public CrlUrlList? GetCrlUrlList()
    {
        if (SigningAuthenticodeCertificate == null)
            return null;

        try
        {
            return new CrlUrlList(SigningAuthenticodeCertificate);
        }
        catch (Exception)
        {
            return null;
        }
    }

        public IEnumerable<byte[]> Icons()
    {
        return (Resources?.Icons).OrEmpty()
            .Select(i => i.AsIco())
            .OfType<byte[]>();
    }

        public IEnumerable<IEnumerable<byte[]>> GroupIcons()
    {
        return (Resources?.GroupIconDirectories).OrEmpty()
            .Select(dir => dir.DirectoryEntries.OrEmpty()
                .SelectMany(iconEntry => iconEntry.AssociatedIcons(this).OrEmpty())
                .Where(icon => icon is not null)
                .Select(icon => icon!.AsIco())
                .OfType<byte[]>());
    }

        public static bool IsPeFile(string file)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read);
        return IsPeFile(fs);
    }

        public static bool IsPeFile(Stream file)
    {
        Span<byte> buffer = stackalloc byte[2];
        file.Seek(0, SeekOrigin.Begin);
        file.Read(buffer);
        return IsPeFile(buffer);
    }

        public static bool IsPeFile(MMFile file)
    {
        if (file.Length < 2)
            return false;

        var buffer = file.AsSpan(0, 2);
        return IsPeFile(buffer);
    }

        public static bool IsPeFile(Span<byte> buf)
    {
        if (buf.Length < 2)
            return false;

        return buf[1] == 0x5a && buf[0] == 0x4d; 
    }
}
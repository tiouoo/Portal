using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using PeNet.Asn1;
using PeNet.Header.Pe;

namespace PeNet.Header.Authenticode;




public class AuthenticodeInfo
{
    private readonly ContentInfo? _contentInfo;
    private readonly PeFile _peFile;

    public AuthenticodeInfo(PeFile peFile)
    {
        _peFile = peFile;

        _contentInfo = _peFile.WinCertificate == null
            ? null
            : new ContentInfo(_peFile.WinCertificate.BCertificate);

        SignerSerialNumber = GetSigningSerialNumber();
        SignedHash = GetSignedHash();
        IsAuthenticodeValid = VerifyHash() && VerifySignature();
        SigningCertificate = GetSigningCertificate();
    }

    public string? SignerSerialNumber { get; }
    public byte[]? SignedHash { get; }
    public bool IsAuthenticodeValid { get; }
    public X509Certificate2? SigningCertificate { get; }

    private X509Certificate2? GetSigningCertificate()
    {
        if (_peFile.WinCertificate?.WCertificateType !=
            WinCertificateType.PkcsSignedData)
            return null;

        var pkcs7 = _peFile.WinCertificate.BCertificate.ToArray();

        
        
        
        
        
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new X509Certificate2(pkcs7)
            : GetSigningCertificateNonWindows(pkcs7);
    }

    private X509Certificate2? GetSigningCertificateNonWindows(byte[] pkcs7)
    {
        
        var signedCms = new SignedCms();
        signedCms.Decode(pkcs7);
        var signerInfos = signedCms.SignerInfos.Cast<SignerInfo>().Where(si =>
                string.Equals(si.Certificate?.SerialNumber, SignerSerialNumber,
                    StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        if (signerInfos.Count == 1) return signerInfos[0].Certificate;
        var numberOfSignerInfos = signerInfos.Count == 0 ? "none" : signerInfos.Count.ToString();
        throw new CryptographicException(
            $"Expected to find one certificate with serial number '{SignerSerialNumber}' but found {numberOfSignerInfos}.");
    }

    private bool VerifySignature()
    {
        var signedCms = new SignedCms();
        var bCert = _peFile.WinCertificate?.BCertificate.ToArray();
        if (bCert is null) return false;
        signedCms.Decode(bCert);

        try
        {
            
            signedCms.CheckSignature(true);
        }
        catch (Exception)
        {
            return false;
        }

        return true;
    }

    private bool VerifyHash()
    {
        if (SignedHash == null) return false;
        
        HashAlgorithm hashAlgorithm;
        switch (SignedHash.Length)
        {
            case 16:
                hashAlgorithm = MD5.Create();
                break;
            case 20:
                hashAlgorithm = SHA1.Create();
                break;
            case 32:
                hashAlgorithm = SHA256.Create();
                break;
            case 48:
                hashAlgorithm = SHA384.Create();
                break;
            case 64:
                hashAlgorithm = SHA512.Create();
                break;
            default:
                return false;
        }

        var hash = ComputeAuthenticodeHashFromPeFile(hashAlgorithm);
        return hash != null && SignedHash.SequenceEqual(hash);
    }

    private byte[]? GetSignedHash()
    {
        if (_contentInfo?.Content is null)
            return null;

        if (_contentInfo?.ContentType != "1.2.840.113549.1.7.2") 
            return null;

        var sd = new SignedData(_contentInfo.Content);
        if (sd.ContentInfo.ContentType != "1.3.6.1.4.1.311.2.1.4") 
            return null;

        var spc = sd.ContentInfo.Content;
        if (spc is null) return null;
        var signedHash = (Asn1OctetString)spc.Nodes[0].Nodes[1].Nodes[1];
        return signedHash.Data;
    }

    private string? GetSigningSerialNumber()
    {
        var asn1 = _contentInfo?.Content;
        if (asn1 is null) return null;
        var x = (Asn1Integer)asn1.Nodes[0].Nodes[4].Nodes[0].Nodes[1]
            .Nodes[1]; 
#if NET48 || NETSTANDARD2_0
            return x.Value.ToHexString().Substring(2).ToUpper();
#else
        return x.Value.ToHexString()[2..].ToUpper();
#endif
    }

    public IEnumerable<byte>? ComputeAuthenticodeHashFromPeFile(HashAlgorithm hash)
    {
        var buff = _peFile.RawFile.ToArray();

        
        
        var offset = Convert.ToInt32(_peFile.ImageNtHeaders?.OptionalHeader.Offset) + 0x40;
        hash.TransformBlock(buff, 0, offset, new byte[offset], 0);

        
        offset += 0x4;

        
        
        var certificateTable = _peFile.ImageNtHeaders?.OptionalHeader.DataDirectory[4];

        
        
        var length = Convert.ToInt32(certificateTable?.Offset) - offset;
        hash.TransformBlock(buff, offset, length, new byte[length], 0);
        offset += length + 0x8; 

        
        
        
        length = Convert.ToInt32(_peFile.ImageNtHeaders?.OptionalHeader.SizeOfHeaders) - offset; 
        hash.TransformBlock(buff, offset, length, new byte[length], 0);

        
        offset = Convert.ToInt32(_peFile.ImageNtHeaders?.OptionalHeader.SizeOfHeaders);

        if (_peFile.WinCertificate is not null)
        {
            length = Convert.ToInt32(_peFile.WinCertificate?.Offset) - offset;
            hash.TransformBlock(buff, offset, length, new byte[length], 0);

            
            offset += length + Convert.ToInt32(certificateTable?.Size);
        }

        
        
        
        
        
        
        
        
        var fileSize = buff.Length;
        if (fileSize > offset)
        {
            length = fileSize - offset;
            if (length != 0) hash.TransformBlock(buff, offset, length, new byte[length], 0);
        }

        
        hash.TransformFinalBlock(buff, 0, 0);
        return hash.Hash;
    }
}
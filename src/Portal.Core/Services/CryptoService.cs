using System.Security.Cryptography;
using System.Text;

namespace Portal.Core.Services;

public static class CryptoService
{
    public static string? Encrypt(string? input)
    {
        if (input is null)
        {
            return null;
        }

        var aes = Aes.Create();
        var key = CredentialsService.CryptoKey;

        if (key is null)
        {
            return input;
        }

        try
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            aes.Key = keyBytes;
            aes.GenerateIV();

            var data = Encoding.UTF8.GetBytes(input);
            var encrypted = aes.EncryptCbc(data, aes.IV);
            var final = aes.IV
                .Concat(encrypted)
                .ToArray();

            return Convert.ToBase64String(final);
        }
        catch
        {
            return input;
        }
    }

    public static string? Decrypt(string? input)
    {
        if (input is null)
        {
            return null;
        }

        var aes = Aes.Create();
        var key = CredentialsService.CryptoKey;

        if (key is null)
        {
            return input;
        }

        try
        {
            var data = Convert.FromBase64String(input);
            var keyBytes = Encoding.UTF8.GetBytes(key);

            var iv = data
                .Take(16)
                .ToArray();
            var encrypted = data
                .Skip(16)
                .ToArray();
            aes.Key = keyBytes;
            var decrypted = aes.DecryptCbc(encrypted, iv);

            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return input;
        }
    }
}

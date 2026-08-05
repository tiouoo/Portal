using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Portal.Bedrock.Standard.Interface;

namespace Portal.Bedrock.Linux;

internal sealed class XboxPreauthService
{
    private const string XboxAppId = "0000000048183522";
    private readonly HttpClient _httpClient = new();
    private readonly string _directory;
    private readonly string _devicePath;
    private readonly string _keyPath;
    private readonly string _idPath;

    public XboxPreauthService(string prefixPath)
    {
        _directory = Path.Combine(prefixPath, "portal-xbox");
        _devicePath = Path.Combine(_directory, "device.json");
        _keyPath = Path.Combine(_directory, "device-key.pem");
        _idPath = Path.Combine(_directory, "device-id.txt");
    }

    public async Task<string> PrepareAsync(BedrockAuthentication account, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        SetPrivatePermissions(_directory, directory: true);
        using var key = LoadOrCreateKey();
        var deviceId = LoadOrCreateDeviceId();
        var publicKey = key.ExportParameters(false);
        var proofKey = new
        {
            alg = "ES256", crv = "P-256", kty = "EC", use = "sig",
            x = Convert.ToBase64String(publicKey.Q.X!), y = Convert.ToBase64String(publicKey.Q.Y!)
        };

        var device = await PostAsync("https://device.auth.xboxlive.com/device/authenticate", new
        {
            RelyingParty = "http://auth.xboxlive.com", TokenType = "JWT",
            Properties = new
            {
                AuthMethod = "ProofOfPossession", Id = deviceId, DeviceType = "Win32",
                Version = "10.0.22631", ProofKey = proofKey
            }
        }, key, cancellationToken);
        var deviceToken = RequiredString(device, "Token");

        var user = await PostAsync("https://user.auth.xboxlive.com/user/authenticate", new
        {
            RelyingParty = "http://auth.xboxlive.com", TokenType = "JWT",
            Properties = new
            {
                AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com",
                RpsTicket = $"d={account.AccessToken}"
            }
        }, key, cancellationToken);
        var userToken = RequiredString(user, "Token");
        var achievements = await PostAsync("https://xsts.auth.xboxlive.com/xsts/authorize", new
        {
            RelyingParty = "http://xboxlive.com", TokenType = "JWT",
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { userToken } }
        }, key, cancellationToken);

        var profile = await AuthorizeSisuAsync("http://xboxlive.com", account.AccessToken, deviceToken,
            proofKey, key, cancellationToken);
        var playfab = await AuthorizeSisuAsync("https://b980a380.minecraft.playfabapi.com/", account.AccessToken,
            deviceToken, proofKey, key, cancellationToken);
        var multiplayer = await AuthorizeSisuAsync("https://multiplayer.minecraft.net/", account.AccessToken,
            deviceToken, proofKey, key, cancellationToken);
        var realms = await AuthorizeSisuAsync("https://pocket.realms.minecraft.net/", account.AccessToken,
            deviceToken, proofKey, key, cancellationToken);
        var licensing = await AuthorizeSisuAsync("http://licensing.xboxlive.com", account.AccessToken,
            deviceToken, proofKey, key, cancellationToken);

        var profileClaims = profile.GetProperty("DisplayClaims").GetProperty("xui")[0];
        var payload = new Dictionary<string, object?>
        {
            ["_account_epoch"] = "legacy",
            ["device_id"] = deviceId,
            ["ecc_private_blob_b64"] = Convert.ToBase64String(ExportBcryptBlob(key)),
            ["device_token"] = deviceToken,
            ["device_token_expiry"] = OptionalString(device, "NotAfter"),
            ["user_token"] = userToken,
            ["user_token_expiry"] = OptionalString(user, "NotAfter"),
            ["xbl_token"] = RequiredString(profile, "Token"),
            ["xbl_token_expiry"] = OptionalString(profile, "NotAfter"),
            ["xbl_xuid"] = OptionalString(profileClaims, "xid") ?? account.Xuid,
            ["xbl_gamertag"] = OptionalString(profileClaims, "gtg") ?? account.Gamertag,
            ["xbl_age_group"] = OptionalString(profileClaims, "agg"),
            ["xbl_uhs"] = OptionalString(profileClaims, "uhs"),
            ["achievements_token"] = RequiredString(achievements, "Token"),
            ["achievements_uhs"] = GetUserHash(achievements),
            ["achievements_expiry"] = OptionalString(achievements, "NotAfter"),
            ["obtained"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        AddSisu(payload, "sisu", "https://b980a380.minecraft.playfabapi.com/", playfab);
        AddSisu(payload, "mp", "https://multiplayer.minecraft.net/", multiplayer);
        AddSisu(payload, "realms", "https://pocket.realms.minecraft.net/", realms);
        AddSisu(payload, "lic", "http://licensing.xboxlive.com", licensing);

        var temporaryPath = _devicePath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(payload,
            new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        SetPrivatePermissions(temporaryPath);
        File.Move(temporaryPath, _devicePath, true);
        SetPrivatePermissions(_devicePath);
        return _devicePath;
    }

    private async Task<JsonElement> AuthorizeSisuAsync(string relyingParty, string accessToken, string deviceToken,
        object proofKey, ECDsa key, CancellationToken cancellationToken)
    {
        var response = await PostAsync("https://sisu.xboxlive.com/authorize", new
        {
            AccessToken = $"t={accessToken}", AppId = XboxAppId, DeviceToken = deviceToken,
            Sandbox = "RETAIL", UseModernGamertag = true, SiteName = "user.auth.xboxlive.com",
            RelyingParty = relyingParty, OfferTermsAcceptance = true, AcceptOffers = true, ProofKey = proofKey
        }, key, cancellationToken);
        return response.GetProperty("AuthorizationToken").Clone();
    }

    private async Task<JsonElement> PostAsync(string url, object body, ECDsa key,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new ByteArrayContent(bytes)
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", "XAL Xbox Live Game (Windows; SDK; 1.0.0.0)");
        request.Headers.TryAddWithoutValidation("x-xbl-contract-version", "1");
        request.Headers.TryAddWithoutValidation("Signature", Sign(key, new Uri(url).AbsolutePath, bytes));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Xbox 预认证失败（{(int)response.StatusCode}）：{SafeError(error)}");
        }
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
    }

    private static string Sign(ECDsa key, string path, byte[] body)
    {
        var version = BigEndian(BitConverter.GetBytes(1));
        var timestamp = BigEndian(BitConverter.GetBytes((DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 11644473600L) * 10000000L));
        using var stream = new MemoryStream();
        stream.Write(version); stream.WriteByte(0);
        stream.Write(timestamp); stream.WriteByte(0);
        stream.Write(Encoding.UTF8.GetBytes("POST")); stream.WriteByte(0);
        stream.Write(Encoding.UTF8.GetBytes(path)); stream.WriteByte(0);
        stream.WriteByte(0);
        stream.Write(body); stream.WriteByte(0);
        var signature = key.SignData(stream.ToArray(), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var result = new byte[76];
        version.CopyTo(result, 0);
        timestamp.CopyTo(result, 4);
        signature.CopyTo(result, 12);
        return Convert.ToBase64String(result);
    }

    private ECDsa LoadOrCreateKey()
    {
        if (File.Exists(_keyPath))
        {
            var loaded = ECDsa.Create();
            loaded.ImportFromPem(File.ReadAllText(_keyPath));
            return loaded;
        }
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(_keyPath, key.ExportPkcs8PrivateKeyPem());
        SetPrivatePermissions(_keyPath);
        return key;
    }

    private string LoadOrCreateDeviceId()
    {
        if (File.Exists(_idPath)) return File.ReadAllText(_idPath).Trim();
        var value = $"{{{Guid.NewGuid().ToString().ToUpperInvariant()}}}";
        File.WriteAllText(_idPath, value);
        SetPrivatePermissions(_idPath);
        return value;
    }

    private static byte[] ExportBcryptBlob(ECDsa key)
    {
        var parameters = key.ExportParameters(true);
        var result = new byte[104];
        BitConverter.GetBytes(0x32534345).CopyTo(result, 0);
        BitConverter.GetBytes(32).CopyTo(result, 4);
        parameters.Q.X!.CopyTo(result, 8);
        parameters.Q.Y!.CopyTo(result, 40);
        parameters.D!.CopyTo(result, 72);
        return result;
    }

    private static void AddSisu(IDictionary<string, object?> payload, string prefix, string relyingParty,
        JsonElement token)
    {
        payload[$"{prefix}_token"] = RequiredString(token, "Token");
        payload[$"{prefix}_rp"] = relyingParty;
        payload[$"{prefix}_uhs"] = GetUserHash(token);
        payload[$"{prefix}_expiry"] = OptionalString(token, "NotAfter");
    }

    private static string? GetUserHash(JsonElement token)
    {
        try { return token.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString(); }
        catch (KeyNotFoundException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static string RequiredString(JsonElement element, string property) =>
        OptionalString(element, property) ?? throw new InvalidOperationException($"Xbox 响应缺少 {property}。");
    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() : null;
    private static byte[] BigEndian(byte[] value) { Array.Reverse(value); return value; }
    private static string SafeError(string value)
    {
        try
        {
            using var json = JsonDocument.Parse(value);
            return json.RootElement.TryGetProperty("XErr", out var xerr) ? $"XErr {xerr.GetInt64()}" : "服务拒绝请求";
        }
        catch (JsonException) { return "服务拒绝请求"; }
    }

    private static void SetPrivatePermissions(string path, bool directory = false)
    {
        try
        {
            File.SetUnixFileMode(path, directory
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (PlatformNotSupportedException)
        {
        }
    }
}

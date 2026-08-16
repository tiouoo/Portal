using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Portal.Core.Minecraft.Classes;

namespace Portal.Core.Operations.Account;

public sealed class BedrockAuthenticationService
{
    private const string ClientId = "0000000048183522";
    private const string Scope = "service::user.auth.xboxlive.com::MBI_SSL";
    private const string ConnectEndpoint = "https://login.live.com/oauth20_connect.srf";
    private const string TokenEndpoint = "https://login.live.com/oauth20_token.srf";
    private readonly HttpClient _httpClient = new();

    public async Task<BedrockAccount> SignInAsync(Action<string, string> showCode,
        CancellationToken cancellationToken = default)
    {
        var deviceCode = await PostFormAsync(ConnectEndpoint, new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["scope"] = Scope,
            ["response_type"] = "device_code"
        }, cancellationToken);

        var deviceCodeValue = GetRequiredString(deviceCode, "device_code");
        var userCode = GetRequiredString(deviceCode, "user_code");
        var verificationUri = deviceCode.TryGetProperty("verification_uri", out var uri)
            ? uri.GetString() ?? "https://www.microsoft.com/link"
            : "https://www.microsoft.com/link";
        showCode(verificationUri, userCode);

        var interval = deviceCode.TryGetProperty("interval", out var intervalValue)
            ? Math.Max(intervalValue.GetInt32(), 5)
            : 5;
        var expiresIn = deviceCode.TryGetProperty("expires_in", out var expiresValue)
            ? expiresValue.GetInt32()
            : 900;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);
            var token = await PostFormAsync(TokenEndpoint, new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["grant_type"] = "device_code",
                ["device_code"] = deviceCodeValue
            }, cancellationToken, false);

            if (token.TryGetProperty("error", out var errorElement))
            {
                var error = errorElement.GetString();
                if (error == "authorization_pending") continue;
                if (error == "slow_down")
                {
                    interval += 5;
                    continue;
                }

                throw new InvalidOperationException(token.TryGetProperty("error_description", out var description)
                    ? description.GetString() ?? error
                    : error);
            }

            return await CreateAccountAsync(token, cancellationToken);
        }

        throw new TimeoutException("微软账户登录已超时，请重试。");
    }

    public async Task<BedrockAccount> RefreshAsync(BedrockAccount account,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account.RefreshToken))
            throw new InvalidOperationException("基岩账户缺少刷新令牌，请重新登录。");

        var token = await PostFormAsync(TokenEndpoint, new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["scope"] = Scope,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = account.RefreshToken
        }, cancellationToken);
        var refreshed = await CreateAccountAsync(token, cancellationToken);
        refreshed.Id = account.Id;
        refreshed.AccountNote = account.AccountNote;
        refreshed.LastLoginTime = account.LastLoginTime;
        return refreshed;
    }

    private async Task<BedrockAccount> CreateAccountAsync(JsonElement token, CancellationToken cancellationToken)
    {
        var accessToken = GetRequiredString(token, "access_token");
        var refreshToken = GetRequiredString(token, "refresh_token");
        var expiresIn = token.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600;
        var (xstsToken, userHash, xuid) = await GetXboxIdentityAsync(accessToken, cancellationToken);
        var profile = await GetProfileAsync(xstsToken, userHash, xuid, cancellationToken);

        return new BedrockAccount
        {
            Gamertag = profile.Gamertag,
            Xuid = xuid,
            AvatarUrl = profile.AvatarUrl,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            LastLoginTime = DateTime.Now
        };
    }

    private async Task<(string Token, string UserHash, string Xuid)> GetXboxIdentityAsync(string accessToken,
        CancellationToken cancellationToken)
    {
        var userResponse = await PostJsonAsync("Xbox User Auth", "https://user.auth.xboxlive.com/user/authenticate", new
        {
            Properties =
                new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = $"t={accessToken}" },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        }, cancellationToken);
        var userToken = GetRequiredString(userResponse, "Token");
        var xstsResponse = await PostJsonAsync("XSTS", "https://xsts.auth.xboxlive.com/xsts/authorize", new
        {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { userToken } },
            RelyingParty = "http://xboxlive.com",
            TokenType = "JWT"
        }, cancellationToken);
        var claims = xstsResponse.GetProperty("DisplayClaims").GetProperty("xui")[0];
        return (GetRequiredString(xstsResponse, "Token"), GetRequiredString(claims, "uhs"),
            GetRequiredString(claims, "xid"));
    }

    private async Task<(string Gamertag, string? AvatarUrl)> GetProfileAsync(string token, string userHash,
        string xuid, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://profile.xboxlive.com/users/xuid({Uri.EscapeDataString(xuid)})/profile/settings?settings=Gamertag,GameDisplayPicRaw");
        request.Headers.TryAddWithoutValidation("Authorization", $"XBL3.0 x={userHash};{token}");
        request.Headers.TryAddWithoutValidation("x-xbl-contract-version", "3");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync("Xbox Profile", response, cancellationToken);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var settings = json.GetProperty("profileUsers")[0].GetProperty("settings");
        string? gamertag = null;
        string? avatarUrl = null;
        foreach (var setting in settings.EnumerateArray())
        {
            var id = setting.GetProperty("id").GetString();
            if (id == "Gamertag") gamertag = setting.GetProperty("value").GetString();
            if (id == "GameDisplayPicRaw") avatarUrl = setting.GetProperty("value").GetString();
        }

        return (gamertag ?? throw new InvalidOperationException("Xbox 档案未返回玩家代号。"), avatarUrl);
    }

    private async Task<JsonElement> PostFormAsync(string endpoint, Dictionary<string, string> values,
        CancellationToken cancellationToken, bool ensureSuccess = true)
    {
        using var response =
            await _httpClient.PostAsync(endpoint, new FormUrlEncodedContent(values), cancellationToken);
        if (ensureSuccess) await EnsureSuccessAsync("Microsoft OAuth", response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    private async Task<JsonElement> PostJsonAsync(string operation, string endpoint, object value,
        CancellationToken cancellationToken)
    {
        using var content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
        await EnsureSuccessAsync(operation, response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    private static async Task EnsureSuccessAsync(string operation, HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"{operation} 请求失败（{(int)response.StatusCode}）：{GetSafeError(body)}");
    }

    private static string GetSafeError(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("XErr", out var xerr)) return $"XErr {xerr.GetInt64()}";
            if (json.RootElement.TryGetProperty("error_description", out var description))
                return description.GetString() ?? "未知错误";
        }
        catch (JsonException)
        {
        }

        return "请检查网络和 Xbox 账户状态";
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidOperationException($"账户服务响应缺少 {propertyName}。");
    }
}
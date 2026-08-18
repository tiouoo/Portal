using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Portal.Bedrock.Xbox;

public sealed class XboxAuthClient : IDisposable
{
	private const string DeviceAuthEndpoint = "https://device.auth.xboxlive.com/device/authenticate";

	private const string UserAuthEndpoint = "https://user.auth.xboxlive.com/user/authenticate";

	private const string XstsEndpoint = "https://xsts.auth.xboxlive.com/xsts/authorize";

	private const string SisuEndpoint = "https://sisu.xboxlive.com/authorize";

	private const string ProfileEndpoint = "https://profile.xboxlive.com/users/batch/profile/settings";

	private const string ProfileRelyingParty = "http://xboxlive.com";

	public const string PlayFabRelyingParty = "https://b980a380.minecraft.playfabapi.com/";

	public const string MultiplayerRelyingParty = "https://multiplayer.minecraft.net/";

	public const string RealmsRelyingParty = "https://pocket.realms.minecraft.net/";

	public const string LicensingRelyingParty = "http://licensing.xboxlive.com";

	private const ulong WindowsFileTimeEpochOffsetSeconds = 11644473600uL;

	private readonly HttpClient _client = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(30L),
		DefaultRequestVersion = HttpVersion.Version20
	};

	public async Task<XboxPreauth> AuthenticateAsync(string msaAccessToken, DeviceIdentity identity, CancellationToken cancellationToken)
	{
		Dictionary<string, string> proofKey = identity.CreateProofKey();
		XboxToken device = TokenFromResponse(await PostSignedJsonAsync<XboxTokenResponse>(identity, DeviceAuthEndpoint, string.Empty, new
		{
			RelyingParty = "http://auth.xboxlive.com",
			TokenType = "JWT",
			Properties = new
			{
				AuthMethod = "ProofOfPossession",
				Id = identity.Id,
				DeviceType = "Win32",
				Version = "10.0.22631",
				ProofKey = proofKey
			}
		}, "device-auth", cancellationToken));
		XboxToken user = TokenFromResponse(await PostSignedJsonAsync<XboxTokenResponse>(identity, UserAuthEndpoint, string.Empty, new
		{
			RelyingParty = "http://auth.xboxlive.com",
			TokenType = "JWT",
			Properties = new
			{
				AuthMethod = "RPS",
				SiteName = "user.auth.xboxlive.com",
				RpsTicket = "t=" + msaAccessToken
			}
		}, "user-auth", cancellationToken));
		XboxTokenWithClaims achievements = null;
		try
		{
			achievements = await XstsAsync(identity, user, "http://xboxlive.com", "xsts-achievements", cancellationToken);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine("警告：Achievements token 不可用：" + ex.Message);
		}
		XboxTokenWithClaims profileToken = await SisuAsync(identity, msaAccessToken, device, proofKey, ProfileRelyingParty, "sisu-profile", cancellationToken);
		XboxTokenWithClaims playFab = await SisuAsync(identity, msaAccessToken, device, proofKey, "https://b980a380.minecraft.playfabapi.com/", "sisu-playfab", cancellationToken);
		XboxTokenWithClaims multiplayer = await SisuAsync(identity, msaAccessToken, device, proofKey, "https://multiplayer.minecraft.net/", "sisu-multiplayer", cancellationToken);
		XboxTokenWithClaims realms = await SisuAsync(identity, msaAccessToken, device, proofKey, "https://pocket.realms.minecraft.net/", "sisu-realms", cancellationToken);
		XboxTokenWithClaims licensing = null;
		try
		{
			licensing = await SisuAsync(identity, msaAccessToken, device, proofKey, "http://licensing.xboxlive.com", "sisu-licensing", cancellationToken);
		}
		catch (Exception ex2)
		{
			Console.Error.WriteLine("警告：Licensing token 不可用：" + ex2.Message);
		}
		string xuid = Required(profileToken.Claims.Xuid, "profile token XUID");
		return new XboxPreauth(await FetchProfileAsync(identity, xuid, profileToken, cancellationToken), identity, device, user, profileToken, achievements, playFab, multiplayer, realms, licensing);
	}

	private async Task<XboxTokenWithClaims> XstsAsync(DeviceIdentity identity, XboxToken user, string relyingParty, string stage, CancellationToken cancellationToken)
	{
		return TokenWithClaims(await PostSignedJsonAsync<XboxTokenResponse>(identity, XstsEndpoint, string.Empty, new
		{
			RelyingParty = relyingParty,
			TokenType = "JWT",
			Properties = new
			{
				SandboxId = "RETAIL",
				UserTokens = new string[1] { user.Value }
			}
		}, stage, cancellationToken));
	}

	private async Task<XboxTokenWithClaims> SisuAsync(DeviceIdentity identity, string msaAccessToken, XboxToken device, Dictionary<string, string> proofKey, string relyingParty, string stage, CancellationToken cancellationToken)
	{
		return TokenWithClaims((await PostSignedJsonAsync<XboxSisuResponse>(identity, SisuEndpoint, string.Empty, new
		{
			AccessToken = "t=" + msaAccessToken,
			AppId = "0000000048183522",
			deviceToken = device.Value,
			Sandbox = "RETAIL",
			UseModernGamertag = true,
			SiteName = "user.auth.xboxlive.com",
			RelyingParty = relyingParty,
			OfferTermsAcceptance = true,
			AcceptOffers = true,
			ProofKey = proofKey
		}, stage, cancellationToken)).AuthorizationToken);
	}

	private async Task<XboxProfile> FetchProfileAsync(DeviceIdentity identity, string xuid, XboxTokenWithClaims token, CancellationToken cancellationToken)
	{
		string text = Required(token.Claims.UserHash, "profile token user hash");
		string authorization = "XBL3.0 x=" + text + ";" + token.Token.Value;
		Dictionary<string, string> dictionary = (await PostSignedJsonAsync<XboxProfileResponse>(identity, ProfileEndpoint, authorization, new
		{
			userIds = new string[1] { xuid },
			settings = new string[4] { "GameDisplayName", "GameDisplayPicRaw", "Gamerscore", "Gamertag" }
		}, "xbox-profile", cancellationToken)).ProfileUsers.FirstOrDefault((XboxProfileUser candidate) => candidate.Id == xuid)?.Settings.ToDictionary<XboxProfileSetting, string, string>((XboxProfileSetting item) => item.Id, (XboxProfileSetting item) => item.Value, StringComparer.Ordinal) ?? throw new InvalidDataException("Xbox Profile API 未返回当前用户。");
		string valueOrDefault = dictionary.GetValueOrDefault("Gamertag");
		string text2 = ((!string.IsNullOrEmpty(valueOrDefault)) ? valueOrDefault : (token.Claims.Gamertag ?? throw new InvalidDataException("Xbox Profile API 缺少 gamertag。")));
		string valueOrDefault2 = dictionary.GetValueOrDefault("GameDisplayName");
		string displayName = ((!string.IsNullOrEmpty(valueOrDefault2)) ? valueOrDefault2 : text2);
		string text3 = dictionary.GetValueOrDefault("GameDisplayPicRaw");
		if (text3 != null && !text3.StartsWith("https://", StringComparison.Ordinal))
		{
			text3 = null;
		}
		else if (text3 != null && !text3.Contains("&format=", StringComparison.Ordinal))
		{
			text3 += "&format=png&w=208&h=208";
		}
		return new XboxProfile(xuid, text2, displayName, dictionary.GetValueOrDefault("Gamerscore"), text3);
	}

	private async Task<T> PostSignedJsonAsync<T>(DeviceIdentity identity, string endpoint, string authorization, object body, string stage, CancellationToken cancellationToken)
	{
		byte[] bodyBytes = JsonSerializer.SerializeToUtf8Bytes(body);
		try
		{
			Uri uri = new Uri(endpoint, UriKind.Absolute);
			string target = (string.IsNullOrEmpty(uri.Query) ? uri.AbsolutePath : (uri.AbsolutePath + uri.Query));
			string value = SignatureHeader(identity, "POST", target, authorization, bodyBytes);
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
			request.Headers.UserAgent.ParseAdd("XAL Xbox Live Game (Windows; SDK; 1.0.0.0)");
			request.Headers.TryAddWithoutValidation("x-xbl-contract-version", (stage == "xbox-profile") ? "2" : "1");
			request.Headers.TryAddWithoutValidation("Signature", value);
			if (authorization.Length != 0)
			{
				request.Headers.TryAddWithoutValidation("Authorization", authorization);
			}
			request.Content = new ByteArrayContent(bodyBytes);
			request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
			using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken);
			byte[] array = await response.Content.ReadAsByteArrayAsync(cancellationToken);
			try
			{
				if (!response.IsSuccessStatusCode)
				{
					throw new InvalidOperationException($"{stage} 返回 HTTP {(int)response.StatusCode}，XErr={ReadXboxError(array)?.ToString() ?? "unknown"}。");
				}
				return JsonSerializer.Deserialize<T>(array) ?? throw new InvalidDataException(stage + " 返回空 JSON。");
			}
			finally
			{
				CryptographicOperations.ZeroMemory(array);
			}
		}
		finally
		{
			CryptographicOperations.ZeroMemory(bodyBytes);
		}
	}

	private static string SignatureHeader(DeviceIdentity identity, string method, string target, string authorization, byte[] body)
	{
		ulong num;
		byte[] bytes;
		byte[] bytes2;
		byte[] bytes3;
		checked
		{
			num = (ulong)Math.Max(0L, DateTimeOffset.UtcNow.ToUnixTimeSeconds()) * 10000000;
			num += WindowsFileTimeEpochOffsetSeconds * 10000000;
			bytes = Encoding.ASCII.GetBytes(method);
			bytes2 = Encoding.ASCII.GetBytes(target);
			bytes3 = Encoding.ASCII.GetBytes(authorization);
		}
		byte[] array = new byte[14 + bytes.Length + 1 + bytes2.Length + 1 + bytes3.Length + 1 + body.Length + 1];
		int num2 = 0;
		BinaryPrimitives.WriteUInt32BigEndian(array.AsSpan(num2, 4), 1u);
		num2 += 4;
		array[num2++] = 0;
		BinaryPrimitives.WriteUInt64BigEndian(array.AsSpan(num2, 8), num);
		num2 += 8;
		array[num2++] = 0;
		bytes.CopyTo(array, num2);
		num2 += bytes.Length;
		array[num2++] = 0;
		bytes2.CopyTo(array, num2);
		num2 += bytes2.Length;
		array[num2++] = 0;
		bytes3.CopyTo(array, num2);
		num2 += bytes3.Length;
		array[num2++] = 0;
		body.CopyTo(array, num2);
		num2 += body.Length;
		array[num2] = 0;
		try
		{
			byte[] array2 = identity.SignP1363(array);
			try
			{
				byte[] array3 = new byte[76];
				BinaryPrimitives.WriteUInt32BigEndian(array3.AsSpan(0, 4), 1u);
				BinaryPrimitives.WriteUInt64BigEndian(array3.AsSpan(4, 8), num);
				array2.CopyTo(array3, 12);
				try
				{
					return Convert.ToBase64String(array3);
				}
				finally
				{
					CryptographicOperations.ZeroMemory(array3);
				}
			}
			finally
			{
				CryptographicOperations.ZeroMemory(array2);
			}
		}
		finally
		{
			CryptographicOperations.ZeroMemory(array);
			CryptographicOperations.ZeroMemory(bytes);
			CryptographicOperations.ZeroMemory(bytes2);
			CryptographicOperations.ZeroMemory(bytes3);
		}
	}

	private static XboxToken TokenFromResponse(XboxTokenResponse response)
	{
		if (string.IsNullOrEmpty(response.Token))
		{
			throw new InvalidDataException("Xbox 服务响应缺少 token。");
		}
		if (string.IsNullOrEmpty(response.NotAfter))
		{
			throw new InvalidDataException("Xbox 服务响应缺少过期时间。");
		}
		return new XboxToken(response.Token, response.NotAfter);
	}

	private static XboxTokenWithClaims TokenWithClaims(XboxTokenResponse response)
	{
		return new XboxTokenWithClaims(TokenFromResponse(response), ClaimsFromDisplay(response.DisplayClaims));
	}

	private static XboxClaims ClaimsFromDisplay(XboxDisplayClaims display)
	{
		Dictionary<string, JsonElement> values = display.Xui.FirstOrDefault();
		return new XboxClaims(Read("xid"), Read("gtg"), Read("uhs"), Read("agg"), Read("mgt"), Read("mgs"), Read("umg"), NormalizePrivileges(Read("prv")));
		string? Read(string name)
		{
			if (values != null && values.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String)
			{
				string text = value.GetString();
				if (!string.IsNullOrEmpty(text))
				{
					return text;
				}
			}
			return null;
		}
	}

	private static string? NormalizePrivileges(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		uint[] array = (from part in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).SelectMany((string part) => part.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			select (!uint.TryParse(part, out var result)) ? ((uint?)null) : new uint?(result) into parsed
			where parsed.HasValue
			select parsed.Value).Distinct().Order().ToArray();
		if (array.Length == 0)
		{
			return null;
		}
		return string.Join(' ', array);
	}

	private static ulong? ReadXboxError(byte[] payload)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(payload);
			string[] array = new string[3] { "XErr", "xerr", "XErrCode" };
			foreach (string propertyName in array)
			{
				if (!jsonDocument.RootElement.TryGetProperty(propertyName, out var value))
				{
					continue;
				}
				if (value.TryGetUInt64(out var value2))
				{
					return value2;
				}
				string text = value.GetString();
				if (text != null)
				{
					if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && ulong.TryParse(text.Substring(2), NumberStyles.HexNumber, null, out value2))
					{
						return value2;
					}
					if (ulong.TryParse(text, out value2))
					{
						return value2;
					}
				}
			}
		}
		catch (JsonException)
		{
		}
		return null;
	}

	private static string Required(string? value, string name)
	{
		if (!string.IsNullOrEmpty(value))
		{
			return value;
		}
		throw new InvalidDataException("Xbox " + name + " 缺失。");
	}

	public void Dispose()
	{
		_client.Dispose();
	}
}

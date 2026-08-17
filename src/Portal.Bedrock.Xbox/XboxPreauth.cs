using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace Portal.Bedrock.Xbox;

public sealed class XboxPreauth : IDisposable
{
	private byte[]? _sessionPayload;

	public XboxProfile Profile { get; }

	public DeviceIdentity Identity { get; }

	public XboxToken Device { get; }

	public XboxToken User { get; }

	public XboxTokenWithClaims ProfileToken { get; }

	public XboxTokenWithClaims? Achievements { get; }

	public XboxTokenWithClaims PlayFab { get; }

	public XboxTokenWithClaims Multiplayer { get; }

	public XboxTokenWithClaims Realms { get; }

	public XboxTokenWithClaims? Licensing { get; }

	public XboxPreauth(XboxProfile profile, DeviceIdentity identity, XboxToken device, XboxToken user, XboxTokenWithClaims profileToken, XboxTokenWithClaims? achievements, XboxTokenWithClaims playFab, XboxTokenWithClaims multiplayer, XboxTokenWithClaims realms, XboxTokenWithClaims? licensing)
	{
		Profile = profile;
		Identity = identity;
		Device = device;
		User = user;
		ProfileToken = profileToken;
		Achievements = achievements;
		PlayFab = playFab;
		Multiplayer = multiplayer;
		Realms = realms;
		Licensing = licensing;
	}

	public byte[] CreateSessionPayload()
	{
		return _sessionPayload ?? (_sessionPayload = BuildSessionPayload());
	}

	private byte[] BuildSessionPayload()
	{
		XboxClaims claims = ProfileToken.Claims;
		byte[] array = Identity.ExportBcryptPrivateBlob();
		try
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>
			{
				["device_id"] = Identity.Id,
				["ecc_private_blob_b64"] = Convert.ToBase64String(array),
				["device_token"] = Device.Value,
				["device_token_expiry"] = Device.NotAfter,
				["user_token"] = User.Value,
				["user_token_expiry"] = User.NotAfter,
				["user_token_expiry_epoch"] = ExpiryEpoch(User.NotAfter).ToString(),
				["xbl_token"] = ProfileToken.Token.Value,
				["xbl_token_expiry"] = ProfileToken.Token.NotAfter,
				["xbl_token_expiry_epoch"] = ExpiryEpoch(ProfileToken.Token.NotAfter).ToString(),
				["xbl_xuid"] = Required(claims.Xuid, "profile token XUID"),
				["xbl_gamertag"] = Required(claims.Gamertag, "profile token gamertag"),
				["xbl_age_group"] = claims.AgeGroup,
				["xbl_uhs"] = Required(claims.UserHash, "profile token user hash"),
				["xbl_modern_gamertag"] = claims.ModernGamertag,
				["xbl_modern_gamertag_suffix"] = claims.ModernGamertagSuffix,
				["xbl_unique_modern_gamertag"] = claims.UniqueModernGamertag,
				["xbl_privileges"] = claims.Privileges,
				["sisu_rp"] = "https://b980a380.minecraft.playfabapi.com/",
				["sisu_token"] = PlayFab.Token.Value,
				["sisu_uhs"] = Required(PlayFab.Claims.UserHash, "PlayFab user hash"),
				["sisu_expiry"] = PlayFab.Token.NotAfter,
				["sisu_expiry_epoch"] = ExpiryEpoch(PlayFab.Token.NotAfter).ToString(),
				["mp_rp"] = "https://multiplayer.minecraft.net/",
				["mp_token"] = Multiplayer.Token.Value,
				["mp_uhs"] = Required(Multiplayer.Claims.UserHash, "Multiplayer user hash"),
				["mp_expiry"] = Multiplayer.Token.NotAfter,
				["mp_expiry_epoch"] = ExpiryEpoch(Multiplayer.Token.NotAfter).ToString(),
				["realms_rp"] = "https://pocket.realms.minecraft.net/",
				["realms_token"] = Realms.Token.Value,
				["realms_uhs"] = Required(Realms.Claims.UserHash, "Realms user hash"),
				["realms_expiry"] = Realms.Token.NotAfter,
				["realms_expiry_epoch"] = ExpiryEpoch(Realms.Token.NotAfter).ToString(),
				["obtained"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
			};
			if (Achievements != null)
			{
				dictionary["achievements_token"] = Achievements.Token.Value;
				dictionary["achievements_uhs"] = Required(Achievements.Claims.UserHash, "Achievements user hash");
				dictionary["achievements_expiry"] = Achievements.Token.NotAfter;
				dictionary["achievements_expiry_epoch"] = ExpiryEpoch(Achievements.Token.NotAfter).ToString();
			}
			if (Licensing != null)
			{
				dictionary["lic_rp"] = "http://licensing.xboxlive.com";
				dictionary["lic_token"] = Licensing.Token.Value;
				dictionary["lic_uhs"] = Required(Licensing.Claims.UserHash, "Licensing user hash");
				dictionary["lic_expiry"] = Licensing.Token.NotAfter;
				dictionary["lic_expiry_epoch"] = ExpiryEpoch(Licensing.Token.NotAfter).ToString();
			}
			return JsonSerializer.SerializeToUtf8Bytes(dictionary);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(array);
		}
	}

	private static long ExpiryEpoch(string value)
	{
		return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUnixTimeSeconds();
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
		if (_sessionPayload != null)
		{
			CryptographicOperations.ZeroMemory(Interlocked.Exchange(ref _sessionPayload, null));
		}
		Identity.Dispose();
	}
}

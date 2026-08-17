using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Portal.Bedrock.Hook;

internal sealed class XUserSession
{
	public ulong Xuid;

	public ulong LocalId;

	public string Gamertag = string.Empty;

	public uint AgeGroup;

	public uint[] Privileges = Array.Empty<uint>();

	public XUserTokenRecord[] Tokens = Array.Empty<XUserTokenRecord>();

	public XUserSigningKey SigningKey;

	public XUserTokenRecord? TokenForRelyingParty(string relyingParty)
	{
		XUserTokenRecord[] tokens = Tokens;
		foreach (XUserTokenRecord xUserTokenRecord in tokens)
		{
			if (xUserTokenRecord.RelyingParty == relyingParty)
			{
				return xUserTokenRecord;
			}
		}
		return null;
	}

	public static XUserSession? ParseSession(byte[] payload)
	{
		XUserSessionDocument xUserSessionDocument;
		try
		{
			xUserSessionDocument = JsonSerializer.Deserialize(payload, XUserSessionJsonContext.Default.XUserSessionDocument) ?? throw new InvalidDataException("null document");
		}
		catch (JsonException)
		{
			return null;
		}
		byte[] array;
		try
		{
			array = Convert.FromBase64String(xUserSessionDocument.ecc_private_blob_b64);
		}
		catch (FormatException)
		{
			return null;
		}
		if (array == null || array.Length != 104)
		{
			return null;
		}
		XUserSigningKey xUserSigningKey = XUserSigningKey.ImportPrivateBlob(array);
		CryptographicOperations.ZeroMemory(array);
		if (xUserSigningKey == null)
		{
			return null;
		}
		if (!TryParseNonZeroDecimal(xUserSessionDocument.xbl_xuid, out var parsed))
		{
			return null;
		}
		ulong localId = (TryParseNonZeroDecimal(xUserSessionDocument.xbl_uhs, out var parsed2) ? parsed2 : parsed);
		if (string.IsNullOrWhiteSpace(xUserSessionDocument.xbl_gamertag))
		{
			return null;
		}
		uint num;
		switch (xUserSessionDocument.xbl_age_group?.ToLowerInvariant())
		{
		case "child":
			num = 1u;
			break;
		case "teen":
		case "teenager":
			num = 2u;
			break;
		case "adult":
			num = 3u;
			break;
		default:
			num = 0u;
			break;
		}
		uint ageGroup = num;
		uint[] privileges = (from part in (xUserSessionDocument.xbl_privileges ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).SelectMany((string part) => part.Split(',', StringSplitOptions.RemoveEmptyEntries))
			select (!uint.TryParse(part, out var result)) ? ((uint?)null) : new uint?(result) into value
			where value.HasValue
			select value.Value).Order().Distinct().ToArray();
		List<XUserTokenRecord> list = new List<XUserTokenRecord>(4);
		if (!TryToken(xUserSessionDocument.xbl_token, xUserSessionDocument.xbl_uhs, "http://xboxlive.com", xUserSessionDocument.xbl_token_expiry_epoch, list))
		{
			return null;
		}
		if (!TryToken(xUserSessionDocument.sisu_token, xUserSessionDocument.sisu_uhs, CheckedRp(xUserSessionDocument.sisu_rp, "https://b980a380.minecraft.playfabapi.com/"), xUserSessionDocument.sisu_expiry_epoch, list))
		{
			return null;
		}
		if (!TryToken(xUserSessionDocument.mp_token, xUserSessionDocument.mp_uhs, CheckedRp(xUserSessionDocument.mp_rp, "https://multiplayer.minecraft.net/"), xUserSessionDocument.mp_expiry_epoch, list))
		{
			return null;
		}
		if (!TryToken(xUserSessionDocument.realms_token, xUserSessionDocument.realms_uhs, CheckedRp(xUserSessionDocument.realms_rp, "https://pocket.realms.minecraft.net/"), xUserSessionDocument.realms_expiry_epoch, list))
		{
			return null;
		}
		bool flag = xUserSessionDocument.lic_token != null;
		bool flag2 = xUserSessionDocument.lic_uhs != null;
		bool flag3 = xUserSessionDocument.lic_expiry_epoch != null;
		if (flag | flag2 | flag3)
		{
			if (!flag || !flag2 || !flag3)
			{
				return null;
			}
			if (!TryToken(xUserSessionDocument.lic_token!, xUserSessionDocument.lic_uhs!, CheckedRp(xUserSessionDocument.lic_rp, "http://licensing.xboxlive.com"), xUserSessionDocument.lic_expiry_epoch!, list))
			{
				return null;
			}
		}
		return new XUserSession
		{
			Xuid = parsed,
			LocalId = localId,
			Gamertag = xUserSessionDocument.xbl_gamertag,
			AgeGroup = ageGroup,
			Privileges = privileges,
			Tokens = list.ToArray(),
			SigningKey = xUserSigningKey
		};
	}

	private static bool TryToken(string value, string userHash, string relyingParty, string expiry, ICollection<XUserTokenRecord> tokens)
	{
		if (!ulong.TryParse(expiry, out var result))
		{
			return false;
		}
		if (value.Length == 0 || !TryParseNonZeroDecimal(userHash, out var _) || relyingParty.Length == 0 || result <= NowEpoch() + 30)
		{
			return false;
		}
		tokens.Add(new XUserTokenRecord
		{
			Token = value,
			UserHash = userHash,
			RelyingParty = relyingParty,
			ExpiresAt = result
		});
		return true;
	}

	private static string CheckedRp(string? provided, string expected)
	{
		if (provided == null || !(provided != expected))
		{
			return expected;
		}
		return string.Empty;
	}

	private static bool TryParseNonZeroDecimal(string value, out ulong parsed)
	{
		parsed = 0uL;
		if (value.Length == 0)
		{
			return false;
		}
		foreach (char c in value)
		{
			if ((c < '0' || c > '9') ? true : false)
			{
				return false;
			}
		}
		if (!ulong.TryParse(value, out parsed) || parsed == 0L)
		{
			return false;
		}
		return true;
	}

	private static ulong NowEpoch()
	{
		return (ulong)Math.Max(0L, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
	}
}

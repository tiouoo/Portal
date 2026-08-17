namespace Portal.Bedrock.Hook;

internal sealed class XUserSessionDocument
{
	public string ecc_private_blob_b64 { get; set; } = string.Empty;

	public string xbl_xuid { get; set; } = string.Empty;

	public string xbl_gamertag { get; set; } = string.Empty;

	public string? xbl_age_group { get; set; }

	public string? xbl_privileges { get; set; }

	public string xbl_token { get; set; } = string.Empty;

	public string xbl_uhs { get; set; } = string.Empty;

	public string xbl_token_expiry_epoch { get; set; } = string.Empty;

	public string sisu_token { get; set; } = string.Empty;

	public string sisu_uhs { get; set; } = string.Empty;

	public string? sisu_rp { get; set; }

	public string sisu_expiry_epoch { get; set; } = string.Empty;

	public string mp_token { get; set; } = string.Empty;

	public string mp_uhs { get; set; } = string.Empty;

	public string? mp_rp { get; set; }

	public string mp_expiry_epoch { get; set; } = string.Empty;

	public string realms_token { get; set; } = string.Empty;

	public string realms_uhs { get; set; } = string.Empty;

	public string? realms_rp { get; set; }

	public string realms_expiry_epoch { get; set; } = string.Empty;

	public string? lic_token { get; set; }

	public string? lic_uhs { get; set; }

	public string? lic_rp { get; set; }

	public string? lic_expiry_epoch { get; set; }
}

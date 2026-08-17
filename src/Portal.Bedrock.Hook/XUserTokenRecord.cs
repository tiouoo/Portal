namespace Portal.Bedrock.Hook;

internal sealed class XUserTokenRecord
{
	public string Token = string.Empty;

	public string UserHash = string.Empty;

	public string RelyingParty = string.Empty;

	public ulong ExpiresAt;
}

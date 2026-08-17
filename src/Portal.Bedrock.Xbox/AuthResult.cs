using System;
using System.Text.Json.Serialization;

namespace Portal.Bedrock.Xbox;

public sealed class AuthResult
{
	[JsonPropertyName("code")]
	public string? Code { get; set; }

	[JsonPropertyName("code_verifier")]
	public string? CodeVerifier { get; set; }

	[JsonPropertyName("redirect_uri")]
	public string? RedirectUri { get; set; }

	[JsonPropertyName("client_id")]
	public string? ClientId { get; set; }

	[JsonPropertyName("access_token")]
	public string? AccessToken { get; set; }

	[JsonPropertyName("refresh_token")]
	public string? RefreshToken { get; set; }

	[JsonPropertyName("expires_in")]
	public int ExpiresIn { get; set; }

	[JsonPropertyName("saved_at")]
	public DateTime SavedAt { get; set; }
}

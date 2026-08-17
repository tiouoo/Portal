using System.Text.Json.Serialization;

namespace Portal.Bedrock.Xbox;

public sealed class XboxTokenResponse
{
	[JsonPropertyName("Token")]
	public string Token { get; init; } = string.Empty;

	[JsonPropertyName("NotAfter")]
	public string NotAfter { get; init; } = string.Empty;

	[JsonPropertyName("DisplayClaims")]
	public XboxDisplayClaims DisplayClaims { get; init; } = new XboxDisplayClaims();
}

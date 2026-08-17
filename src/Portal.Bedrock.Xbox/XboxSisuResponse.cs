using System.Text.Json.Serialization;

namespace Portal.Bedrock.Xbox;

public sealed class XboxSisuResponse
{
	[JsonPropertyName("AuthorizationToken")]
	public XboxTokenResponse AuthorizationToken { get; init; } = new XboxTokenResponse();
}

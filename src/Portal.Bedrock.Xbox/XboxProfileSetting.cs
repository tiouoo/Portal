using System.Text.Json.Serialization;

namespace Portal.Bedrock.Xbox;

public sealed class XboxProfileSetting
{
	[JsonPropertyName("id")]
	public string Id { get; init; } = string.Empty;

	[JsonPropertyName("value")]
	public string Value { get; init; } = string.Empty;
}

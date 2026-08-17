using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Portal.Bedrock.Xbox;

public sealed class XboxProfileUser
{
	[JsonPropertyName("id")]
	public string Id { get; init; } = string.Empty;

	[JsonPropertyName("settings")]
	public List<XboxProfileSetting> Settings { get; init; } = new List<XboxProfileSetting>();
}

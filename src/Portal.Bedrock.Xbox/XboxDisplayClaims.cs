using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Portal.Bedrock.Xbox;

public sealed class XboxDisplayClaims
{
	[JsonPropertyName("xui")]
	public List<Dictionary<string, JsonElement>> Xui { get; init; } = new List<Dictionary<string, JsonElement>>();
}

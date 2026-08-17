using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Portal.Bedrock.Xbox;

public sealed class XboxProfileResponse
{
	[JsonPropertyName("profileUsers")]
	public List<XboxProfileUser> ProfileUsers { get; init; } = new List<XboxProfileUser>();
}

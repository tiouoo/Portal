using System.Text.Json;
using System.Text.Json.Serialization;

namespace Portal.Bedrock.Core;

internal static class BedrockJsonOptions
{
	public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = false,
		Converters = 
		{
			(JsonConverter)new MinecraftGameTypeVersionConverter(),
			(JsonConverter)new MinecraftBuildTypeVersionConverter(),
			(JsonConverter)new ArchitectureJsonConverter()
		}
	};

	public static T? Deserialize<T>(string json)
	{
		return JsonSerializer.Deserialize<T>(json, Options);
	}
}

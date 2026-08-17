using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Portal.Bedrock.Core;

public class MinecraftGameTypeVersionConverter : JsonConverter<MinecraftGameTypeVersion>
{
	public override MinecraftGameTypeVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.String)
		{
			string text = reader.GetString()?.ToLower();
			MinecraftGameTypeVersion result = text switch
			{
				"preview" => MinecraftGameTypeVersion.Preview, 
				"release" => MinecraftGameTypeVersion.Release, 
				"beta" => MinecraftGameTypeVersion.Beta, 
				_ => MinecraftGameTypeVersion.Release, 
			};
			return result;
		}
		return MinecraftGameTypeVersion.Release;
	}

	public override void Write(Utf8JsonWriter writer, MinecraftGameTypeVersion value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(value.ToString().ToLower());
	}
}

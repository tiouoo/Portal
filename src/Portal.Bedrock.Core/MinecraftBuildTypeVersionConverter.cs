using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Portal.Bedrock.Core;

public class MinecraftBuildTypeVersionConverter : JsonConverter<MinecraftBuildTypeVersion>
{
	public override MinecraftBuildTypeVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.String)
		{
			string text = reader.GetString()?.ToUpper();
			MinecraftBuildTypeVersion result = ((text == "UWP") ? MinecraftBuildTypeVersion.UWP : ((!(text == "GDK")) ? MinecraftBuildTypeVersion.UNKNOWN : MinecraftBuildTypeVersion.GDK));
			return result;
		}
		return MinecraftBuildTypeVersion.UNKNOWN;
	}

	public override void Write(Utf8JsonWriter writer, MinecraftBuildTypeVersion value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(value.ToString().ToUpper());
	}
}

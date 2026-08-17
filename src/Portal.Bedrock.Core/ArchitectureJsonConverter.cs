using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Portal.Bedrock.Core;

public class ArchitectureJsonConverter : JsonConverter<Architecture>
{
	public override Architecture Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return Architecture.X86;
		}
		if (reader.TokenType == JsonTokenType.String)
		{
			string text = reader.GetString();
			if (string.IsNullOrEmpty(text))
			{
				return Architecture.X86;
			}
			string text2 = text.ToLowerInvariant();
			Architecture result;
			switch (text2)
			{
			case "x64":
			case "amd64":
			case "x86_64":
				result = Architecture.X64;
				break;
			case "x86":
			case "ia32":
			case "i386":
				result = Architecture.X86;
				break;
			case "arm":
			case "arm32":
				result = Architecture.Arm;
				break;
			case "arm64":
			case "aarch64":
				result = Architecture.Arm64;
				break;
			case "wasm":
			case "webassembly":
				result = Architecture.Wasm;
				break;
			case "s390x":
				result = Architecture.S390x;
				break;
			case "loongarch64":
				result = Architecture.LoongArch64;
				break;
			case "armv6":
				result = Architecture.Armv6;
				break;
			case "ppc64le":
				result = Architecture.Ppc64le;
				break;
			default:
				throw new SwitchExpressionException(text);
			}
			return result;
		}
		if (reader.TokenType == JsonTokenType.Number)
		{
			return (Architecture)reader.GetInt32();
		}
		throw new JsonException($"Cant covert this token. TokenType: {reader.TokenType}");
	}

	public override void Write(Utf8JsonWriter writer, Architecture value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(value.ToString().ToLowerInvariant());
	}
}

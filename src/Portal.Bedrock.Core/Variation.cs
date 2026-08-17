using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace Portal.Bedrock.Core;

public class Variation
{
	[JsonPropertyName("Arch")]
	[JsonConverter(typeof(ArchitectureJsonConverter))]
	public Architecture Arch { get; set; }

	[JsonPropertyName("ArchivalStatus")]
	public int ArchivalStatus { get; set; }

	[JsonPropertyName("OSbuild")]
	public string OSBuild { get; set; } = string.Empty;

	[JsonPropertyName("MetaData")]
	public List<string> MetaData { get; set; } = new List<string>();

	[JsonPropertyName("MD5")]
	public string MD5 { get; set; } = string.Empty;
}

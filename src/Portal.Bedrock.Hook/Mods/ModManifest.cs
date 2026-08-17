using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Portal.Bedrock.Hook.Mods;

internal sealed class ModManifest
{
	public string? Id { get; set; }

	public string Name { get; set; } = string.Empty;

	public string Entry { get; set; } = string.Empty;

	public string? Version { get; set; }

	public string? Author { get; set; }

	public string? Description { get; set; }

	[JsonPropertyName("type")]
	public string ModType { get; set; } = string.Empty;

	public uint? ApiVersion { get; set; }

	public ulong? InjectDelayMs { get; set; }

	public string? InjectReady { get; set; }

	public bool RequiresSymbolPack { get; set; }

	public List<string> RequiredSymbols { get; set; } = new List<string>();

	public bool Required { get; set; }

	public List<string> VerifyExports { get; set; } = new List<string>();

	public List<string> VerifyModules { get; set; } = new List<string>();

	public bool NotifySuccess { get; set; }

	public List<string> LogAliases { get; set; } = new List<string>();
}

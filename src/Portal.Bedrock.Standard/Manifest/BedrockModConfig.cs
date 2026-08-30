using System.Text.Json.Serialization;

namespace Portal.Bedrock.Standard.Manifest;

public sealed class BedrockModConfig
{
    [JsonPropertyName("mods")]
    public List<BedrockModEntry> Mods { get; set; } = [];
}

public sealed class BedrockModEntry
{
    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("preload")]
    public bool Preload { get; set; }

    [JsonPropertyName("delayMs")]
    public int DelayMs { get; set; } = 5;
}

public sealed record BedrockModInfo(string FilePath, long FileSize, BedrockModEntry Config,
    bool IsPackage = false, string? PackageName = null, string? PackageVersion = null,
    string? PackageDescription = null, string? PackageRoot = null)
{
    public string FileName => Path.GetFileName(FilePath);
}

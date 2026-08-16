using System.Text.Json.Serialization;

namespace Portal.Module.Multiplayer;

public sealed class GravityConeManifest
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; }
    [JsonPropertyName("gravityCone")] public required OnlineComponentManifest GravityCone { get; init; }
    [JsonPropertyName("easyTier")] public required OnlineComponentManifest EasyTier { get; init; }
}

public sealed class OnlineComponentManifest
{
    [JsonPropertyName("version")] public required string Version { get; init; }
    [JsonPropertyName("packages")] public required Dictionary<string, OnlinePackageManifest> Packages { get; init; }
}

public sealed class OnlinePackageManifest
{
    [JsonPropertyName("url")] public required string Url { get; init; }
    [JsonPropertyName("fileName")] public required string FileName { get; init; }
    [JsonPropertyName("archiveType")] public required string ArchiveType { get; init; }
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("sha256")] public required string Sha256 { get; init; }
    [JsonPropertyName("executable")] public string? Executable { get; init; }
}

public sealed record GravityConeInstallation(string CliPath, string EasyTierDirectory);

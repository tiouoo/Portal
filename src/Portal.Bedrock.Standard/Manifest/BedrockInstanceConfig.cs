using System.Text.Json.Serialization;

namespace Portal.Bedrock.Standard.Manifest;

public class BedrockInstanceConfig
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; }
    [JsonPropertyName("buildType")] public BedrockBuildType BuildType { get; set; }
    [JsonPropertyName("type")] public BedrockInstanceReleaseType Type { get; set; }
    [JsonPropertyName("enableIndependentInstance")] public bool EnableIndependentInstance { get; set; } = false;
    [JsonIgnore] public string InstancePath { get; set; }
}

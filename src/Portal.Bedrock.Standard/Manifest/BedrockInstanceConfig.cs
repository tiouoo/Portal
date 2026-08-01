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
    [JsonPropertyName("enableLauncherSharedData")] public bool EnableLauncherSharedData { get; set; } = false;
    [JsonPropertyName("enableMouseLock")] public bool EnableMouseLock { get; set; } = false;
    [JsonPropertyName("enableMouseLockForGdk")] public bool EnableMouseLockForGdk { get; set; } = false;
    [JsonPropertyName("mouseLockInset")] public int MouseLockInset { get; set; } = 2;
    [JsonPropertyName("mouseLockHotkey")] public string MouseLockHotkey { get; set; } = "Ctrl+Alt";
    [JsonPropertyName("launchArguments")] public string LaunchArguments { get; set; } = string.Empty;
    [JsonPropertyName("enableCreatorEditor")] public bool EnableCreatorEditor { get; set; }
    [JsonIgnore] public string InstancePath { get; set; }
}

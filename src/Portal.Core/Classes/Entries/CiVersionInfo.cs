using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Portal.Core.Classes.Entries;

public partial class CiVersionInfo : ObservableObject
{
    [JsonPropertyName("type")]
    [ObservableProperty]
    public partial string Type { get; set; } = string.Empty;

    [JsonPropertyName("build-time")]
    [ObservableProperty]
    public partial DateTime BuildTime { get; set; }

    [JsonPropertyName("action")]
    [ObservableProperty]
    public partial string Action { get; set; } = string.Empty;

    [JsonPropertyName("commit")]
    [ObservableProperty]
    public partial string Commit { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    [ObservableProperty]
    public partial string Version { get; set; } = string.Empty;

    [JsonPropertyName("version_title")]
    [ObservableProperty]
    public partial string VersionTitle { get; set; } = string.Empty;
}
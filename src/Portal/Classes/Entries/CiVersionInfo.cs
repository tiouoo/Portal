using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace Portal.Classes.Entries;

public partial class CiVersionInfo : ObservableObject
{
    [JsonProperty("type")]
    [ObservableProperty]
    public partial string Type { get; set; } = string.Empty;

    [JsonProperty("build-time")]
    [ObservableProperty]
    public partial DateTime BuildTime { get; set; }

    [JsonProperty("action")]
    [ObservableProperty]
    public partial string Action { get; set; } = string.Empty;

    [JsonProperty("commit")]
    [ObservableProperty]
    public partial string Commit { get; set; } = string.Empty;

    [JsonProperty("version")]
    [ObservableProperty]
    public partial string Version { get; set; } = string.Empty;
}
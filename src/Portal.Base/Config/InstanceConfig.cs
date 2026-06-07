using System.Text.Json.Serialization;
using Portal.Base.Enum;

namespace Portal.Base.Config;

public class InstanceConfig
{
    [JsonPropertyName("instanceName")] 
    public string InstanceName { get; set; }
    
    [JsonPropertyName("instanceType")]
    public InstanceType InstanceType { get; set; }
}
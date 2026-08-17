using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Portal.Bedrock.Core;

public class BuildDatabase
{
	[JsonPropertyName("CreationTime")]
	public DateTime CreationTime { get; set; }

	[JsonExtensionData]
	public Dictionary<string, object> ExtensionData { get; set; } = new Dictionary<string, object>();

	[JsonIgnore]
	public IAsyncEnumerable<KeyValuePair<string, BuildInfo>> Builds => GetBuildsFromExtensionData();

	private async IAsyncEnumerable<KeyValuePair<string, BuildInfo>> GetBuildsFromExtensionData()
	{
		JsonElement jsonElement = default(JsonElement);
		foreach (var (_, value) in ExtensionData)
		{
			int num;
			if (value is JsonElement)
			{
				jsonElement = (JsonElement)value;
				num = ((jsonElement.ValueKind == JsonValueKind.Object) ? 1 : 0);
			}
			else
			{
				num = 0;
			}
			if (num == 0)
			{
				continue;
			}
			foreach (JsonProperty item in jsonElement.EnumerateObject())
			{
				BuildInfo buildInfo = JsonSerializer.Deserialize<BuildInfo>(item.Value.GetRawText(), BedrockJsonOptions.Options);
				if (buildInfo != null)
				{
					yield return new KeyValuePair<string, BuildInfo>(item.Name, buildInfo);
				}
				await Task.Yield();
			}
			jsonElement = default(JsonElement);
		}
	}
}

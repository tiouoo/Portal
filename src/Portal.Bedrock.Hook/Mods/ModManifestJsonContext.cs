using System.Text.Json;
using System.Text.Json.Serialization;

namespace Portal.Bedrock.Hook.Mods;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(ModManifest))]
[JsonSerializable(typeof(ManifestBundle))]
internal sealed partial class ModManifestJsonContext : JsonSerializerContext
{
}

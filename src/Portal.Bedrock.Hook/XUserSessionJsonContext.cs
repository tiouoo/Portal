using System.Text.Json.Serialization;

namespace Portal.Bedrock.Hook;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(XUserSessionDocument))]
internal sealed partial class XUserSessionJsonContext : JsonSerializerContext
{
}

using System.Text.Json;

namespace Portal.Bedrock.Hook.Mods;

internal static class ModManifestJsonContextHolder
{
	public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true
	};

	public static ModManifest? Parse(string text)
	{
		try
		{
			return JsonSerializer.Deserialize(text, ModManifestJsonContext.Default.ModManifest);
		}
		catch (JsonException)
		{
			try
			{
				return JsonSerializer.Deserialize(text, ModManifestJsonContext.Default.ManifestBundle)?.Manifest;
			}
			catch (JsonException)
			{
			}
			return null;
		}
	}

	public static string Serialize(ModManifest manifest)
	{
		return JsonSerializer.Serialize(manifest, ModManifestJsonContext.Default.ModManifest);
	}
}

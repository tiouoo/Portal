namespace Portal.Bedrock.Core;

public readonly record struct BedrockColor(byte R, byte G, byte B)
{
	public string ToHex(bool includeHash = false)
	{
		return includeHash ? $"#{R:X2}{G:X2}{B:X2}" : $"{R:X2}{G:X2}{B:X2}";
	}
}

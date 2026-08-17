namespace Portal.Bedrock.Hook.Mods;

internal struct BlModApiV1
{
	public uint ApiVersion;

	public BlStringView ModId;

	public BlStringView ModName;

	public nint OnLoad;

	public nint OnUnload;
}

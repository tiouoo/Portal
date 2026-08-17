namespace Portal.Bedrock.Hook.Mods;

internal static class BlAbi
{
	public const uint BlApiVersion1 = 1u;

	public const uint BlPathGameDir = 1u;

	public const uint BlPathModsDir = 2u;

	public const uint BlPathCacheDir = 3u;

	public const uint BlPathUiResourcePackDir = 4u;

	public const uint BlLogDebug = 0u;

	public const uint BlLogInfo = 1u;

	public const uint BlLogWarn = 2u;

	public const uint BlLogError = 3u;

	public const uint BlEventBootstrapComplete = 1u;

	public const uint BlEventRenderFrame = 2u;

	public const uint BlEventUiFrame = 3u;

	public const uint BlEventResourceReload = 4u;

	public const uint BlEventShutdown = 5u;

	public const uint BlEventTick = 6u;

	public const uint BlEventKey = 7u;

	public const uint BlEventWorldEnter = 8u;

	public const uint BlEventChat = 9u;

	public const uint BlEventCreatedLevel = 10u;

	public const uint BlEventStartGamePacket = 11u;

	public const uint BlEventSetLocalPlayerAsInit = 12u;

	public const uint BlEventLocalPlayerBound = 13u;

	public const uint BlEventPlayerAction = 14u;

	public const uint BlEventBlockAction = 15u;

	public const uint BlEventClientJoinLevel = 16u;

	public const uint BlEventRender3D = 17u;

	public const uint BlRegistryEvent = 1u;

	public const uint BlRegistryUiPanel = 2u;

	public const uint BlRegistryResource = 3u;

	public const uint BlRegistryTextPanel = 4u;

	public const uint BlRegistryFeatureToggle = 5u;

	public const uint BlRegistryFeaturePanel = 6u;

	public const string BlModMainV1 = "bl_mod_main_v1";
}

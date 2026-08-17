namespace Portal.Bedrock.Hook.Mods;

internal struct BlHostApiV1
{
	public uint ApiVersion;

	public uint Reserved;

	public nint Log;

	public nint Register;

	public nint GetHostVersion;

	public nint GetPath;

	public nint ResolveSymbol;

	public nint GetRuntimeInfo;

	public nint PathExists;

	public nint CreateDir;

	public nint ReadTextFile;

	public nint WriteTextFile;

	public nint UiBeginWindow;

	public nint UiEndWindow;

	public nint UiText;

	public nint UiBulletText;

	public nint UiButton;

	public nint UiCheckbox;

	public nint UiSliderFloat;

	public nint UiDragFloat;

	public nint UiProgressBar;

	public nint UiSeparator;

	public nint UiSameLine;

	public nint HudBeginBlock;

	public nint HudTextLine;

	public nint HudEndBlock;

	public nint RegisterBedrockScreen;

	public nint RequestBedrockScreen;

	public nint UiShowToast;
}

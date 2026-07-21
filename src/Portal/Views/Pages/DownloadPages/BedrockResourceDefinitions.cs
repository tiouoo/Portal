namespace Portal.Views.Pages.DownloadPages;

public static class BedrockResourceDefinitions
{
    // CurseForge identifies Minecraft Bedrock separately from Java Edition (432).
    private const int CurseForgeBedrockGameId = 78022;
    public static JavaResourceDefinition BehaviorPack { get; } =
        new(JavaResourceKind.BedrockBehaviorPack, "行为包", string.Empty, null, true, false, false, CurseForgeBedrockGameId);
    public static JavaResourceDefinition ResourcePack { get; } =
        new(JavaResourceKind.BedrockResourcePack, "资源包", string.Empty, null, true, false, false, CurseForgeBedrockGameId);
    public static JavaResourceDefinition World { get; } =
        new(JavaResourceKind.BedrockWorld, "世界", string.Empty, null, true, false, false, CurseForgeBedrockGameId);
    public static JavaResourceDefinition WorldTemplate { get; } =
        new(JavaResourceKind.BedrockWorldTemplate, "世界模板", string.Empty, null, true, false, false, CurseForgeBedrockGameId);
}

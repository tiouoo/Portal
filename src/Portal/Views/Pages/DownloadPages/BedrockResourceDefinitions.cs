using Portal.Core.Minecraft.Models;
using Portal.Localization;

namespace Portal.Views.Pages.DownloadPages;

public static class BedrockResourceDefinitions
{
    private const int CurseForgeBedrockGameId = 78022;

    public static JavaResourceDefinition BehaviorPack { get; } =
        new(JavaResourceKind.BedrockBehaviorPack,
            CommonLanguageManager.Instance.bedrockResourceSearch_behaviorPack.CurrentValue(), string.Empty, null, true,
            false, false,
            CurseForgeBedrockGameId);

    public static JavaResourceDefinition ResourcePack { get; } =
        new(JavaResourceKind.BedrockResourcePack,
            CommonLanguageManager.Instance.bedrockResourceSearch_resourcePack.CurrentValue(), string.Empty, null, true,
            false, false,
            CurseForgeBedrockGameId);

    public static JavaResourceDefinition World { get; } =
        new(JavaResourceKind.BedrockWorld, CommonLanguageManager.Instance.bedrockResourceSearch_world.CurrentValue(),
            string.Empty, null, true, false, false, CurseForgeBedrockGameId);

    public static JavaResourceDefinition WorldTemplate { get; } =
        new(JavaResourceKind.BedrockWorldTemplate,
            CommonLanguageManager.Instance.bedrockResourceSearch_worldTemplate.CurrentValue(), string.Empty, null, true,
            false, false,
            CurseForgeBedrockGameId);
}
using Portal.Core.Minecraft;

namespace Portal.Desktop;

internal static class AppSetup
{
#if WINDOWS || LINUX
    public static void RegisterBedrockLauncher()
    {
#if WINDOWS
        MinecraftLaunchService.DefaultBedrockLauncherFactory =
            config => new Bedrock.BedrockLaunch(config);
        Bedrock.Standard.Interface.BedrockInstallationService.DefaultInstaller =
            new Bedrock.BedrockInstaller();
        Bedrock.Standard.Interface.BedrockToolsService.Default =
            new Bedrock.BedrockWindowsToolsService();
#elif LINUX
        MinecraftLaunchService.DefaultBedrockLauncherFactory =
            config => new Bedrock.Linux.BedrockLaunch(config);
        Bedrock.Standard.Interface.BedrockInstallationService.DefaultInstaller =
            new Bedrock.Linux.BedrockInstaller();
#endif
    }
#endif
}

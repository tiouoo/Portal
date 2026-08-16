using Portal.Bedrock;
using Portal.Bedrock.Standard.Interface;
using Portal.Core.Minecraft;

namespace Portal.Desktop;

internal static class AppSetup
{
#if WINDOWS || LINUX
    public static void RegisterBedrockLauncher()
    {
#if WINDOWS
        MinecraftLaunchService.DefaultBedrockLauncherFactory =
            config => new BedrockLaunch(config);
        BedrockInstallationService.DefaultInstaller =
            new BedrockInstaller();
        BedrockToolsService.Default =
            new BedrockWindowsToolsService();
#elif LINUX
        MinecraftLaunchService.DefaultBedrockLauncherFactory =
            config => new Bedrock.Linux.BedrockLaunch(config);
        Bedrock.Standard.Interface.BedrockInstallationService.DefaultInstaller =
            new Bedrock.Linux.BedrockInstaller();
#endif
    }
#endif
}
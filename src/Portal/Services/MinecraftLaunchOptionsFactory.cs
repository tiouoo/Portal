using Portal.Const;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;

namespace Portal.Services;

public static class MinecraftLaunchOptionsFactory
{
    public static MinecraftLaunchOptions Create(Action<MinecraftLogSession>? openLog = null) => new()
    {
        Account = Data.ConfigEntry.UsingMinecraftMinecraftAccount,
        JavaRuntimes = Data.ConfigEntry.JavaRuntimes,
        DefaultJavaRuntime = Data.ConfigEntry.DefaultJavaRuntime,
        WindowWidth = Data.ConfigEntry.MinecraftWindowWidth,
        WindowHeight = Data.ConfigEntry.MinecraftWindowHeight,
        MaxMemory = Data.ConfigEntry.MinecraftMaxMemory,
        AutoSetJavaHighPerformanceGpu = Data.ConfigEntry.AutoSetJavaHighPerformanceGpu,
        AutoOptimizeMemoryBeforeGameLaunch = Data.ConfigEntry.AutoOptimizeMemoryBeforeGameLaunch,
        WindowTitle = Data.ConfigEntry.OverrideMinecraftWindowTitle,
        JvmArguments = Data.ConfigEntry.JvmArgs,
        BeforeLaunchCommand = Data.ConfigEntry.BeforeLaunchCommand,
        AfterLaunchCommand = Data.ConfigEntry.AfterLaunchCommand,
        WrapperCommand = Data.ConfigEntry.PackagedCommand,
        GameStarted = PortalVisibilityService.OnGameStarted,
        GameExited = PortalVisibilityService.OnGameExited,
        AccountRefreshed = UpdateMicrosoftAccount,
        OpenLog = openLog,
        InstallMissingJava = (version, progress, token) => JavaAutoInstallCoordinator.EnsureAsync(version, progress, token)
    };

    private static void UpdateMicrosoftAccount(MinecraftAccount original, MinecraftAccount refreshed)
    {
        var accounts = Data.ConfigEntry.MinecraftAccounts;
        var index = accounts.IndexOf(original);
        if (index >= 0)
            accounts[index] = refreshed;
        Data.ConfigEntry.UsingMinecraftMinecraftAccount = refreshed;
    }
}

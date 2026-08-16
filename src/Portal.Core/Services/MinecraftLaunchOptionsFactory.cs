using Portal.Core.App.Events;
using Portal.Core.Const;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;

namespace Portal.Core.Services;

public static class MinecraftLaunchOptionsFactory
{
    public static MinecraftLaunchOptions Create(MinecraftInstance instance, Action<MinecraftLogSession>? openLog = null)
    {
        var javaConfig = instance.JavaConfig;
        var overrideAdvanced = javaConfig?.EnableOverrideAdvancedOptions == true;

        return new MinecraftLaunchOptions
        {
            Account = Data.ConfigEntry.UsingMinecraftMinecraftAccount,
            BedrockAccount = Data.ConfigEntry.UsingBedrockAccount,
            EnableBedrockAccountInjection = Data.ConfigEntry.EnableBedrockAccountInjection,
            EnableGameOverlay = overrideAdvanced ? javaConfig.EnableGameOverlay : Data.ConfigEntry.EnableGameOverlay,
            IsFullscreen = overrideAdvanced ? javaConfig.EnableFullscreen : Data.ConfigEntry.EnableFullscreen,
            ShowGameOverlay = UiEvents.ShowGameOverlay,
            JavaRuntimes = Data.ConfigEntry.JavaRuntimes,
            DefaultJavaRuntime = Data.ConfigEntry.DefaultJavaRuntime,
            WindowWidth = overrideAdvanced ? javaConfig.MinecraftWindowWidth : Data.ConfigEntry.MinecraftWindowWidth,
            WindowHeight = overrideAdvanced ? javaConfig.MinecraftWindowHeight : Data.ConfigEntry.MinecraftWindowHeight,
            MaxMemory = Data.ConfigEntry.MinecraftMaxMemory,
            AutoSetJavaHighPerformanceGpu = Data.ConfigEntry.AutoSetJavaHighPerformanceGpu,
            AutoOptimizeMemoryBeforeGameLaunch = Data.ConfigEntry.AutoOptimizeMemoryBeforeGameLaunch,
            SetChineseLanguageOnLaunch = overrideAdvanced ? javaConfig.AutoSetChineseLanguage : Data.ConfigEntry.AutoSetChineseLanguage,
            WindowTitle = overrideAdvanced && !string.IsNullOrWhiteSpace(javaConfig.OverrideMinecraftWindowTitle)
                ? javaConfig.OverrideMinecraftWindowTitle
                : Data.ConfigEntry.OverrideMinecraftWindowTitle,
            JvmArguments = overrideAdvanced && !string.IsNullOrWhiteSpace(javaConfig.JvmArgs)
                ? javaConfig.JvmArgs
                : Data.ConfigEntry.JvmArgs,
            BeforeLaunchCommand = overrideAdvanced && !string.IsNullOrWhiteSpace(javaConfig.BeforeLaunchCommand)
                ? javaConfig.BeforeLaunchCommand
                : Data.ConfigEntry.BeforeLaunchCommand,
            AfterLaunchCommand = overrideAdvanced && !string.IsNullOrWhiteSpace(javaConfig.AfterLaunchCommand)
                ? javaConfig.AfterLaunchCommand
                : Data.ConfigEntry.AfterLaunchCommand,
            WrapperCommand = overrideAdvanced && !string.IsNullOrWhiteSpace(javaConfig.PackagedCommand)
                ? javaConfig.PackagedCommand
                : Data.ConfigEntry.PackagedCommand,
            GameStarted = PortalVisibilityService.OnGameStarted,
            GameExited = PortalVisibilityService.OnGameExited,
            AccountRefreshed = UpdateMicrosoftAccount,
            BedrockAccountRefreshed = UpdateBedrockAccount,
            OpenLog = openLog,
            InstallMissingJava = (version, progress, token) => JavaAutoInstallCoordinator.EnsureAsync(version, progress, token),
            ResourceSourceRoots = ResolveResourceSourceRoots(instance)
        };
    }

    private static IReadOnlyList<string> ResolveResourceSourceRoots(MinecraftInstance instance)
    {
        var currentFolder = instance.FolderPath;
        try
        {
            return Data.ConfigEntry.MinecraftFolders
                .Where(folder => !string.Equals(folder.FolderPath, currentFolder, StringComparison.OrdinalIgnoreCase))
                .SelectMany(MinecraftResourceRoots.Resolve)
                .Where(path => Directory.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static void UpdateMicrosoftAccount(MinecraftAccount original, MinecraftAccount refreshed)
    {
        var accounts = Data.ConfigEntry.MinecraftAccounts;
        var index = accounts.IndexOf(original);
        if (index >= 0)
            accounts[index] = refreshed;
        Data.ConfigEntry.UsingMinecraftMinecraftAccount = refreshed;
    }

    private static void UpdateBedrockAccount(BedrockAccount original, BedrockAccount refreshed)
    {
        var accounts = Data.ConfigEntry.BedrockAccounts;
        var index = accounts.IndexOf(original);
        if (index >= 0) accounts[index] = refreshed;
        Data.ConfigEntry.UsingBedrockAccount = refreshed;
    }
}

using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Threading;
using Portal.Const;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Views.SubWindows;

namespace Portal.Services;

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
            ShowGameOverlay = ShowOverlay,
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
            InstallMissingJava = (version, progress, token) => JavaAutoInstallCoordinator.EnsureAsync(version, progress, token)
        };
    }

    private static void ShowOverlay(Process process, MinecraftInstance inst)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var overlay = new OverlayWindow(process, inst);
                TextOptions.SetTextRenderingMode(overlay, TextRenderingMode.Antialias);
                TextOptions.SetTextHintingMode(overlay, TextHintingMode.Light);
                TextOptions.SetBaselinePixelAlignment(overlay, BaselinePixelAlignment.Aligned);
                overlay.Show();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"显示游戏覆盖层失败: {ex.Message}");
            }
        });
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

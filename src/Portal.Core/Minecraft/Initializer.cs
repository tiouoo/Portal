using MinecraftLaunch;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Utilities;
using Portal.Core.Minecraft.Services;
using Portal.Core.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft;

public static class MinecraftCoreInitializer
{
    public static string AppVersion { get; private set; } = string.Empty;

    public static void Initialize(MinecraftCoreInitializeOptions options)
    {
        AppVersion = options.AppVersion ?? string.Empty;
        Logger.Info(string.Format(LogLanguageManager.Instance.minecraft_coreInitialize.CurrentValue(), options.MaxThread,
            options.MaxFragment, options.MaxRetryCount,
            options.IsEnableFragment
                ? CommonLanguageManager.Instance.minecraft_enabled.CurrentValue()
                : CommonLanguageManager.Instance.minecraft_disabled.CurrentValue()));
        InitializeHelper.Initialize(settings =>
        {
            settings.MaxThread = options.MaxThread;
            settings.MaxFragment = options.MaxFragment;
            settings.MaxRetryCount = options.MaxRetryCount;
            settings.MinecraftMetadataSource = options.MinecraftMetadataSource;
            settings.MinecraftFileSource = options.MinecraftFileSource;
            settings.IsEnableFragment = options.IsEnableFragment;
            settings.CurseForgeApiKey = CredentialsService.CurseForgeApiKey;
            settings.UserAgent = $"Portal/{options.AppVersion}";
            settings.DisableSystemProxy = options.DisableSystemProxy;
            settings.ProxyServer = options.ProxyServer;
        });
        if (options.EnableCustomUserAgent && !string.IsNullOrEmpty(options.CustomUserAgent))
        {
            HttpUtil.FlurlClient.Headers.AddOrReplace("User-Agent", options.CustomUserAgent);
            Logger.Info(string.Format(LogLanguageManager.Instance.minecraft_customUserAgentApplied.CurrentValue(), options.CustomUserAgent));
        }
        else
        {
            HttpUtil.FlurlClient.Headers.AddOrReplace("User-Agent",
                $"Portal/{options.AppVersion}");
            Logger.Debug(string.Format(LogLanguageManager.Instance.minecraft_defaultUserAgentApplied.CurrentValue(), $"Portal/{options.AppVersion}"));
        }

        // Iridium 下载源自动选择：注册活跃镜像源（天跑）并按配置应用模式。
        ResourceSourceService.Initialize();
    }
}

public class MinecraftCoreInitializeOptions
{
    public string AppVersion { get; set; }
    public string? CustomUserAgent { get; set; }
    public bool EnableCustomUserAgent { get; set; } = false;
    public bool DisableSystemProxy { get; set; }
    public string? ProxyServer { get; set; }
    public int MaxThread { get; set; } = 16;
    public int MaxFragment { get; set; } = 16;
    public int MaxRetryCount { get; set; } = 4;
    public DownloadSourceMode MinecraftMetadataSource { get; set; } = DownloadSourceMode.Auto;
    public DownloadSourceMode MinecraftFileSource { get; set; } = DownloadSourceMode.Auto;
    public bool IsEnableFragment { get; set; } = false;
}
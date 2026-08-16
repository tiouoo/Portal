using MinecraftLaunch;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Utilities;
using Portal.Core.App.Service;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft;

public static class MinecraftCoreInitializer
{
    public static string AppVersion { get; private set; } = string.Empty;

    public static void Initialize(MinecraftCoreInitializeOptions options)
    {
        AppVersion = options.AppVersion ?? string.Empty;
        Logger.Info(
            $"初始化 Minecraft 核心：线程数 {options.MaxThread}，分片数 {options.MaxFragment}，重试次数 {options.MaxRetryCount}，分片下载 {(options.IsEnableFragment ? "已启用" : "未启用")}");
        InitializeHelper.Initialize(settings =>
        {
            settings.MaxThread = options.MaxThread;
            settings.MaxFragment = options.MaxFragment;
            settings.MaxRetryCount = options.MaxRetryCount;
            settings.MinecraftMetadataSource = options.MinecraftMetadataSource;
            settings.MinecraftFileSource = options.MinecraftFileSource;
            settings.ModrinthSource = options.ModrinthSource;
            settings.CurseForgeSource = options.CurseForgeSource;
            settings.IsEnableFragment = options.IsEnableFragment;
            settings.CurseForgeApiKey = CredentialsService.CurseForgeApiKey;
            settings.UserAgent = $"Portal/{options.AppVersion}";
            settings.DisableSystemProxy = options.DisableSystemProxy;
            settings.ProxyServer = options.ProxyServer;
        });
        if (options.EnableCustomUserAgent && !string.IsNullOrEmpty(options.CustomUserAgent))
        {
            HttpUtil.FlurlClient.Headers.AddOrReplace("User-Agent", options.CustomUserAgent);
            Logger.Info("Minecraft 核心已应用自定义 User-Agent: " + options.CustomUserAgent);
        }
        else
        {
            HttpUtil.FlurlClient.Headers.AddOrReplace("User-Agent",
                $"Portal/{options.AppVersion}");
            Logger.Debug("Minecraft 核心已应用 Portal 默认 User-Agent: " + $"Portal/{options.AppVersion}");
        }
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
    public DownloadSourceMode ModrinthSource { get; set; } = DownloadSourceMode.Auto;
    public DownloadSourceMode CurseForgeSource { get; set; } = DownloadSourceMode.Auto;
    public bool IsEnableFragment { get; set; } = false;
}
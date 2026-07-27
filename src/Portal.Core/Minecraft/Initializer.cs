namespace Portal.Core.Minecraft;

using MinecraftLaunch;
using Tio.Avalonia.Standard.Modules.DiskIO;

public static class MinecraftCoreInitializer
{
    public static void Initialize(MinecraftCoreInitializeOptions options)
    {
        Logger.Info($"初始化 Minecraft 核心：线程数 {options.MaxThread}，分片数 {options.MaxFragment}，重试次数 {options.MaxRetryCount}，镜像 {(options.IsEnableMirror ? "已启用" : "未启用")}，分片下载 {(options.IsEnableFragment ? "已启用" : "未启用")}");
        InitializeHelper.Initialize(settings =>
        {
            settings.MaxThread = options.MaxThread;
            settings.MaxFragment = options.MaxFragment;
            settings.MaxRetryCount = options.MaxRetryCount;
            settings.IsEnableMirror = options.IsEnableMirror;
            settings.IsEnableFragment = options.IsEnableFragment;
            settings.CurseForgeApiKey = ServiceCredentials.CurseForgeApiKey;
            settings.UserAgent = $"Portal/{options.AppVersion}";
            settings.DisableSystemProxy = options.DisableSystemProxy;
            settings.ProxyServer = options.ProxyServer;
        });
        if (options.EnableCustomUserAgent && !string.IsNullOrEmpty(options.CustomUserAgent))
        {
            MinecraftLaunch.Utilities.HttpUtil.FlurlClient.Headers.AddOrReplace("User-Agent", options.CustomUserAgent);
            Logger.Info("Minecraft 核心已应用自定义 User-Agent");
        }
        else
        {
            MinecraftLaunch.Utilities.HttpUtil.FlurlClient.Headers.AddOrReplace("User-Agent", $"Portal/{options.AppVersion}");
            Logger.Debug("Minecraft 核心已应用 Portal 默认 User-Agent");
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
    public bool IsEnableMirror { get; set; } = false;
    public bool IsEnableFragment { get; set; } = false;
}

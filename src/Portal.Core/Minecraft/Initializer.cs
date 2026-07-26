namespace Portal.Core.Minecraft;

using MinecraftLaunch;

public static class MinecraftCoreInitializer
{
    public static void Initialize(MinecraftCoreInitializeOptions options)
    {
        InitializeHelper.Initialize(settings =>
        {
            settings.MaxThread = options.MaxThread;
            settings.MaxFragment = options.MaxFragment;
            settings.MaxRetryCount = options.MaxRetryCount;
            settings.IsEnableMirror = options.IsEnableMirror;
            settings.IsEnableFragment = options.IsEnableFragment;
            settings.CurseForgeApiKey = ServiceCredentials.CurseForgeApiKey;
            settings.UserAgent = $"Portal/{options.AppVersion}";
        });
        if (options.EnableCustomUserAgent && !string.IsNullOrEmpty(options.CustomUserAgent))
            MinecraftLaunch.Utilities.HttpUtil.FlurlClient.Headers.AddOrReplace("User-Agent", options.CustomUserAgent);
        else
            MinecraftLaunch.Utilities.HttpUtil.FlurlClient.Headers.AddOrReplace("User-Agent", $"Portal/{options.AppVersion}");
    }
}

public class MinecraftCoreInitializeOptions
{
    public string AppVersion { get; set; }
    public string? CustomUserAgent { get; set; }
    public bool EnableCustomUserAgent { get; set; } = false;
    public int MaxThread { get; set; } = 16;
    public int MaxFragment { get; set; } = 16;
    public int MaxRetryCount { get; set; } = 4;
    public bool IsEnableMirror { get; set; } = false;
    public bool IsEnableFragment { get; set; } = false;
}

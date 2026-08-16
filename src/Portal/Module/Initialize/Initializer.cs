using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls.Notifications;
using Portal.Classes.Entries;
using Portal.Classes.Enums;
using Portal.Const;
using Portal.Core;
using Portal.Core.App.Service;
using Portal.Core.Minecraft;
using Portal.Bedrock.Standard.Interface;
using Portal.Views;
using Tio.Avalonia.Standard.Modules.Events;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Modules.Platform;
using Tio.Avalonia.Standard.Tab.Common;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Helpers;

namespace Portal.Module.Initialize;

public static partial class Initializer
{
    public static void BedrockPackageImport()
    {
        Logger.Info("开始初始化基岩版包导入服务");
        Config.Initialize();
        Logger.Info("基岩版包导入服务初始化完成");
    }

    public static void App()
    {
        Logger.Info("开始初始化应用服务");
        Config.Initialize();
        BedrockNetworkConfiguration.Configure(Data.ConfigEntry.DisableSystemProxy,
            Data.ConfigEntry.EnableProxyServer ? Data.ConfigEntry.ProxyServer : null, Data.Instance.UserAgent,
            Data.ConfigEntry.EnableGithubMirror, Data.ConfigEntry.GithubMirrorUrl,
            Data.ConfigEntry.GithubMirrorMode == Classes.Enums.GithubMirrorMode.Direct,
            Data.ConfigEntry.EnableFragmentDownload, Data.ConfigEntry.DownloadMaxFragmentCount,
            Data.ConfigEntry.DownloadMaxRetryCount);
        MinecraftCoreInitializer.Initialize(new MinecraftCoreInitializeOptions()
        {
            AppVersion = AppVersionService.Instance.Version.VersionTitle,
            EnableCustomUserAgent = Data.ConfigEntry.EnableCustomUserAgent,
            CustomUserAgent = Data.ConfigEntry.CustomUserAgent,
            DisableSystemProxy = Data.ConfigEntry.DisableSystemProxy,
            ProxyServer = Data.ConfigEntry.EnableProxyServer ? Data.ConfigEntry.ProxyServer : null,
            MaxThread = Data.ConfigEntry.DownloadMaxThreadCount,
            MaxFragment = Data.ConfigEntry.DownloadMaxFragmentCount,
            MaxRetryCount = Data.ConfigEntry.DownloadMaxRetryCount,
            MinecraftMetadataSource = Data.ConfigEntry.MinecraftMetadataSource,
            MinecraftFileSource = Data.ConfigEntry.MinecraftFileSource,
            ModrinthSource = Data.ConfigEntry.ModrinthSource,
            CurseForgeSource = Data.ConfigEntry.CurseForgeSource,
            IsEnableFragment = Data.ConfigEntry.EnableFragmentDownload
        });
        Logger.Info("应用服务初始化完成");
    }
}

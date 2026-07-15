using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls.Notifications;
using Portal.Classes.Entries;
using Portal.Classes.Enums;
using Portal.Const;
using Portal.Core;
using Portal.Core.Minecraft;
using Portal.Views;
using Tio.Avalonia.Standard.Modules.Events;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Modules.Platform;
using Tio.Avalonia.Standard.Tab.Common;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Helpers;

namespace Portal.Module.Initialize;

public static partial class Initializer
{
    public static void App()
    {
        Config.Initialize();
        MinecraftCoreInitializer.Initialize(new MinecraftCoreInitializeOptions()
        {
            AppVersion = Data.Instance.Version.VersionTitle,
            EnableCustomUserAgent = Data.ConfigEntry.EnableCustomUserAgent,
            CustomUserAgent = Data.ConfigEntry.CustomUserAgent,
            MaxThread = Data.ConfigEntry.DownloadMaxThreadCount,
            MaxFragment = Data.ConfigEntry.DownloadMaxFragmentCount,
            MaxRetryCount = Data.ConfigEntry.DownloadMaxRetryCount,
            IsEnableMirror = Data.ConfigEntry.EnableMinecraftMirror,
            IsEnableFragment = Data.ConfigEntry.EnableFragmentDownload
        });
    }
}
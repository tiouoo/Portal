using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Portal.Classes.Entries;
using Portal.Classes.Enums;
using Portal.Const;
using Portal.Core;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Instance;
using Portal.Module.Update;
using Portal.Views;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Events;
using Tio.Avalonia.Standard.Modules.Platform;
using Tio.Avalonia.Standard.Tab.Common;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Helpers;

namespace Portal.Module.Initialize;

public static partial class Initializer
{
    /// <summary>
    /// 初始化窗口（OOBE）显示前的轻量初始化：仅应用主题相关配置，
    /// 并提前设置 NotificationGateway.IsToastFunc，避免账户登录等流程中的通知调用抛出空引用
    /// </summary>
    public static void Oobe()
    {
        ThemeHelper.SetThemeColor(Data.ConfigEntry.ThemeColor);
        ThemeHelper.ToggleTheme(Data.ConfigEntry.Theme);
        NotificationGateway.IsToastFunc = () => Data.ConfigEntry.NoticeWay == NoticeWay.Toast;
    }

    public static async Task UiAsync()
    {
        File.WriteAllText(ConfigPath.AppPathDataPath,
            Process.GetCurrentProcess().MainModule.FileName);

        ThemeHelper.SetThemeColor(Data.ConfigEntry.ThemeColor);
        ThemeHelper.ToggleTheme(Data.ConfigEntry.Theme);
        if (Data.ConfigEntry.EnableCustomForegroundColor)
        {
            ConfigEntry.SetForegroundColor(Data.ConfigEntry.ForegroundColor);
        }
        
        LoopGc.BeginLoop();

        Functions.CreateNewTabWindowFunc = _ => new TabWindow(false);
        NotificationGateway.IsToastFunc = () => Data.ConfigEntry.NoticeWay == NoticeWay.Toast;

        Events.CoreSaveSettings += Portal.App.Method.SaveConfig;

        if (Data.ConfigEntry.BackgroundMode == BackgroundMode.Default)
        {
            Application.Current.Resources.Remove("BackGroundOpacity");
            Application.Current.Resources.Remove("TranslucentBackGroundOpacity");
        }
        else
        {
            Application.Current.Resources["BackGroundOpacity"] = Data.ConfigEntry.ControlOpacity;
            Application.Current.Resources["TranslucentBackGroundOpacity"] = Data.ConfigEntry.TranslucentControlOpacity;
        }

        if (Data.ConfigEntry.EnableCheckAutoUpdate && Data.Instance.Version.Type != "dev")
            _ = CheckUpdate();
        
        await LoadUiDataAsync();

        InitializationEvents.RaiseAfterUiLoaded();
    }

    public static async Task LoadBedrockPackageImportDataAsync()
    {
        var folders = Data.ConfigEntry.MinecraftFolders.ToArray();
        var instances = await Task.Run(() => InstanceManager.Instance.ScanBedrock(folders));
        InstanceManager.Instance.ApplyInstances(instances);
    }

    private static async Task LoadUiDataAsync()
    {
        var folders = Data.ConfigEntry.MinecraftFolders.ToArray();
        var instancesTask = Task.Run(() => InstanceManager.Instance.ScanAll(folders));
        var newsTask = Task.Run(NewsService.InitializeFromCache);

        var instances = await instancesTask;
        InstanceManager.Instance.ApplyInstances(instances);
        Logger.Info($"实例数据加载完成，共加载 {instances.Count} 个实例");

        await newsTask;
        NewsService.RaiseNewsUpdated();
        _ = Task.Run(NewsService.FetchAndRefreshAsync);
    }

    private static async Task CheckUpdate()
    {
        var result = await UpdateChecker.Check(null, true);
        switch (result)
        {
            case null:
                Data.UiProperty.FoundNewVersion = false;
                Data.UiProperty.IsLatestVersion = false;
                return;
            case "latest":
                Data.UiProperty.IsLatestVersion = true;
                return;
            default:
                Data.UiProperty.NewVersion = result;
                Data.UiProperty.FoundNewVersion = true;
                break;
        }
    }
}

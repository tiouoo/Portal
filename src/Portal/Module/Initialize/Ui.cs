using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Portal.Core.App.Events;
using Portal.Core.Classes;
using Portal.Core.Classes.Config;
using Portal.Core.Classes.Entries;
using Portal.Core.Const;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Module.Initialize;
using Portal.Core.Module.Multiplayer;
using Portal.Core.Module.Update;
using Portal.Core.Minecraft.Services;
using Portal.Core.Services;
using Portal.Core.Services.SystemResources;
using Portal.Services;
using Portal.Views;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Events;
using Tio.Avalonia.Standard.Modules.Platform;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Common;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Helpers;
using OverlayWindow = Portal.Views.OverlayWindow;

namespace Portal.Module.Initialize;

public static partial class Initializer
{
    public static void Oobe()
    {
        ThemeHelper.SetThemeColor(Data.ConfigEntry.ThemeColor);
        ThemeHelper.ToggleTheme(Data.ConfigEntry.Theme);
        NotificationGateway.IsToastFunc = () => Data.ConfigEntry.NoticeWay == NoticeWay.Toast;
    }

    public static async Task UiAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.Info("开始初始化主界面数据和后台服务。");
        Logger.Info($"正在写入当前应用程序路径：{ConfigPath.AppPathDataPath}");
        File.WriteAllText(ConfigPath.AppPathDataPath,
            Process.GetCurrentProcess().MainModule.FileName);

        ThemeHelper.SetThemeColor(Data.ConfigEntry.ThemeColor);
        ThemeHelper.ToggleTheme(Data.ConfigEntry.Theme);
        if (Data.ConfigEntry.EnableCustomForegroundColor)
            ConfigEntry.SetForegroundColor(Data.ConfigEntry.ForegroundColor);

        LoopGc.BeginLoop();
        MemoryOptimizationService.StartAutomaticWorkingSetTrim();

        Functions.CreateNewTabWindowFunc = _ => new TabWindow(false);
        NotificationGateway.IsToastFunc = () => Data.ConfigEntry.NoticeWay == NoticeWay.Toast;
        UiEvents.BackgroundAppearanceChanged += TabWindow.ApplyBackgroundToAllWindows;
        UiEvents.ImageMaskChanged += TabWindow.ApplyImageMaskToAllWindows;
        UiEvents.AppScaleChanged += AppScaling.ApplyScale;
        UiEvents.ShowGameOverlay = (process, instance) =>
        {
            var overlay = new OverlayWindow(process, instance);
            TextOptions.SetTextRenderingMode(overlay, TextRenderingMode.Antialias);
            TextOptions.SetTextHintingMode(overlay, TextHintingMode.Light);
            TextOptions.SetBaselinePixelAlignment(overlay, BaselinePixelAlignment.Aligned);
            overlay.Show();
        };

        Events.CoreSaveSettings += ConfigSaver.SaveConfig;

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

        if (Data.ConfigEntry.EnableCheckAutoUpdate && AppVersionService.Instance.Version.Type != "dev")
        {
            Logger.Info("已启用自动更新检查，正在后台检查更新。");
            CheckUpdate().Forget("检查应用更新");
        }

        Logger.Info("正在后台预取联机中继节点列表。");
        GravityConeRelayClient.Instance.PrefetchAsync().Forget("预取联机中继节点列表");

        Logger.Info("正在初始化最近游玩、屏蔽列表和系统资源服务。");
        RecentPlayListService.Initialize();
        BlockListService.Initialize();
        SystemResourceService.Initialize();
        await LoadUiDataAsync();

        InitializationEvents.RaiseAfterUiLoaded();
        Logger.Info($"主界面数据和后台服务初始化完成，耗时 {stopwatch.ElapsedMilliseconds} ms。");
    }

    public static async Task LoadBedrockPackageImportDataAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var folders = Data.ConfigEntry.MinecraftFolders.ToArray();
        Logger.Info($"开始扫描基岩版实例，目标文件夹数量：{folders.Length}。");
        var instances = await Task.Run(() => InstanceManager.Instance.ScanBedrock(folders));
        InstanceManager.Instance.ApplyInstances(instances);
        Logger.Info($"基岩版实例扫描完成，共加载 {instances.Count} 个实例，耗时 {stopwatch.ElapsedMilliseconds} ms。");
    }

    private static async Task LoadUiDataAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var folders = Data.ConfigEntry.MinecraftFolders.ToArray();
        Logger.Info($"开始扫描全部 Minecraft 实例，目标文件夹数量：{folders.Length}。");
        var instancesTask = Task.Run(() => InstanceManager.Instance.ScanAll(folders));
        var newsTask = Task.Run(NewsService.InitializeFromCache);

        var instances = await instancesTask;
        InstanceManager.Instance.ApplyInstances(instances);
        Logger.Info($"实例数据加载完成，共加载 {instances.Count} 个实例，耗时 {stopwatch.ElapsedMilliseconds} ms。");

        await newsTask;
        Logger.Info("新闻缓存加载完成，正在通知界面刷新并后台获取最新新闻。");
        NewsService.RaiseNewsUpdated();
        Task.Run(NewsService.FetchAndRefreshAsync).Forget("刷新最新新闻");
    }

    private static async Task CheckUpdate()
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await UpdateChecker.Check(null, true);
        Logger.Info($"应用更新检查完成，结果：{result ?? "无可用结果"}，耗时 {stopwatch.ElapsedMilliseconds} ms。");
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
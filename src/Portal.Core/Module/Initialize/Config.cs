using System.Reflection;
using System.Text.Json;
using Portal.Core.Classes;
using Portal.Core.Classes.Config;
using Portal.Core.Classes.Entries;
using Portal.Core.Const;
using Portal.Core.Json;
using Portal.Core.Minecraft.Instance.Bedrock;
using Portal.Core.Services;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Events;
using Tio.Avalonia.Standard.Modules.Extensions;
using Index = Portal.Core.Module.AggregatedSearch.Index;

namespace Portal.Core.Module.Initialize;

public class Config
{
    public static List<object> FailedSettingKeys { get; } = [];

    public static void Initialize()
    {
        Logger.Info("开始加载应用配置");
        Helper.TryCreateFolder(ConfigPath.UserDataRootPath);
        Helper.TryCreateFolder(ConfigPath.TempFolderPath);
        Helper.TryCreateFolder(ConfigPath.UpdateFolderPath);
        BedrockDataPathResolver.EnsurePortalDataDirectories();

        var isFirstRun = !File.Exists(ConfigPath.SettingDataPath);
        if (isFirstRun)
        {
            Logger.Info("未找到配置文件，正在创建默认配置");
            File.WriteAllText(ConfigPath.SettingDataPath, new ConfigEntry().AsJson());
        }

        Logger.Info($"配置文件夹：{ConfigPath.UserDataRootPath}");

        InitializationEvents.RaiseBeforeReadSettings();

        try
        {
            var configText = File.ReadAllText(ConfigPath.SettingDataPath);
            Data.ConfigEntry = string.IsNullOrWhiteSpace(configText)
                ? new ConfigEntry()
                : JsonSerializer.Deserialize<ConfigEntry>(configText, PortalJson.Options) ?? new ConfigEntry();
        }
        catch (JsonException exception)
        {
            FailedSettingKeys.Add($"Setting load failed at: {exception.Path}");
            Logger.Error($"读取或解析配置文件失败：{ConfigPath.SettingDataPath}", exception);
            try
            {
                var backupPath = ConfigPath.SettingDataPath + ".bak";
                File.Copy(ConfigPath.SettingDataPath, backupPath, true);
                Logger.Error($"配置文件解析失败，已备份到：{backupPath}");
            }
            catch (Exception backupEx)
            {
                Logger.Error($"备份损坏配置文件失败：{ConfigPath.SettingDataPath}", backupEx);
            }

            Data.ConfigEntry = new ConfigEntry();
        }

        Logger.MinimumLevel = Data.ConfigEntry.MinimumLogLevel;
        Data.ConfigEntry.Shortcuts ??= new ShortcutConfig();

        if (!Enum.IsDefined(typeof(UpdateSource), Data.ConfigEntry.UpdateSource))
        {
            Logger.Info($"旧配置中的更新源 {Data.ConfigEntry.UpdateSource} 已失效，回退为 CNB。");
            Data.ConfigEntry.UpdateSource = UpdateSource.Cnb;
        }

        if (Data.ConfigEntry.UsingBedrockAccount is { } selectedBedrockAccount)
            Data.ConfigEntry.UsingBedrockAccount = Data.ConfigEntry.BedrockAccounts
                .FirstOrDefault(account =>
                    account.Id == selectedBedrockAccount.Id || account.Xuid == selectedBedrockAccount.Xuid);
        Data.ConfigEntry.UsingBedrockAccount ??= Data.ConfigEntry.BedrockAccounts.FirstOrDefault();

        if (isFirstRun) Data.ConfigEntry.IsInitialized = false;

        if (FailedSettingKeys.Count > 0) Logger.Error($"Setting load with errors: {FailedSettingKeys.AsJson()}");

        Data.ConfigEntry.MinecraftAccounts.CollectionChanged += (_, _) => Index.MarkDirty();
        Data.ConfigEntry.BedrockAccounts.CollectionChanged += (_, _) => Index.MarkDirty();
        Data.ConfigEntry.AuthServers.CollectionChanged += (_, _) => Index.MarkDirty();

        var version = AppVersionService.Instance.Version;
        Logger.Info($"已加载版本信息：{version.VersionTitle} ({version.Type})");
        Data.UiProperty.OverrideUpdateChannel = Data.ConfigEntry.UpdateSource == UpdateSource.Github
            ? version.Type
            : "release";

        const string RESOURCE_NAME1 = "Portal.Core.Assets.package-type.txt";
        var assembly1 = Assembly.GetExecutingAssembly();
        var stream1 = assembly1.GetManifestResourceStream(RESOURCE_NAME1);
        using var reader1 = new StreamReader(stream1!);
        var result1 = reader1.ReadToEnd();
        Data.Instance.PackageType = string.IsNullOrWhiteSpace(result1) ? "portable" : result1.Trim().ToLowerInvariant();
        Logger.Info($"已识别安装包类型：{Data.Instance.PackageType}");

        ConfigIdentifyExtension.Window(Data.ConfigEntry);

        Helper.ClearFolder(ConfigPath.TempFolderPath);
        Logger.Debug("已清理临时目录");
        ConfigSaver.SaveConfig();

        Data.UiProperty.ConfigLoaded = true;
        ConfigIdentifyExtension.MinecraftFolder(Data.ConfigEntry);

        Logger.Info($"配置加载完成，已配置 {Data.ConfigEntry.MinecraftFolders.Count} 个 Minecraft 文件夹");

        InitializationEvents.RaiseBeforeUiLoaded();
    }
}
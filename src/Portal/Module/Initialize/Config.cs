using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Portal.Classes.Entries;
using Portal.Const;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Instance.Bedrock;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Events;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Modules.Helper;

namespace Portal.Module.Initialize;

public class Config
{
    public static List<object> FailedSettingKeys { get; } = [];

    public static void Initialize()
    {
        Helper.TryCreateFolder(ConfigPath.UserDataRootPath);
        Helper.TryCreateFolder(ConfigPath.TempFolderPath);
        Helper.TryCreateFolder(ConfigPath.UpdateFolderPath);
        BedrockDataPathResolver.EnsurePortalDataDirectories();

        var isFirstRun = !File.Exists(ConfigPath.SettingDataPath);
        if (isFirstRun)
            File.WriteAllText(ConfigPath.SettingDataPath, new ConfigEntry().AsJson());

        Logger.Info($"配置文件夹：{ConfigPath.UserDataRootPath}");

        InitializationEvents.RaiseBeforeReadSettings();

        try
        {
            var settings = new JsonSerializerSettings
            {
                Error = (_, item) =>
                {
                    FailedSettingKeys.Add(item);
                    item.ErrorContext.Handled = true;
                },
                MissingMemberHandling = MissingMemberHandling.Ignore
            };

            Data.ConfigEntry = JsonConvert.DeserializeObject<ConfigEntry>(
                File.ReadAllText(ConfigPath.SettingDataPath), settings
            ) ?? new ConfigEntry();
        }
        catch (Exception ex)
        {
            FailedSettingKeys.Add($"Setting completely load failed: {ex.Message}");
            try
            {
                // 解析失败时先备份损坏的配置文件，避免默认配置直接覆盖用户数据
                var backupPath = ConfigPath.SettingDataPath + ".bak";
                File.Copy(ConfigPath.SettingDataPath, backupPath, true);
                Logger.Error($"配置文件解析失败，已备份到：{backupPath}");
            }
            catch (Exception backupEx)
            {
                Logger.Error($"备份损坏配置文件失败：{backupEx.Message}");
            }
            Data.ConfigEntry = new ConfigEntry();
        }

        if (isFirstRun) Data.ConfigEntry.IsInitialized = false;

        if (FailedSettingKeys.Count > 0) Logger.Error($"Setting load with errors: {FailedSettingKeys.AsJson()}");

        // 账户与认证服务器变更时使聚合搜索索引失效（实例集合的失效逻辑在 UiProperty 中）
        Data.ConfigEntry.MinecraftAccounts.CollectionChanged += (_, _) => AggregatedSearch.Index.MarkDirty();
        Data.ConfigEntry.AuthServers.CollectionChanged += (_, _) => AggregatedSearch.Index.MarkDirty();

        const string RESOURCE_NAME = "Portal.version-ci.txt";
        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream(RESOURCE_NAME);
        using var reader = new StreamReader(stream!);
        var result = reader.ReadToEnd();
        Data.Instance.Version = JsonConvert.DeserializeObject<CiVersionInfo>(result) ?? new CiVersionInfo()
        {
            Type = "dev",
            VersionTitle = "local-build"
        };
        Data.UiProperty.OverrideUpdateChannel = Data.Instance.Version.Type;

        const string RESOURCE_NAME1 = "Portal.package-type.txt";
        var assembly1 = Assembly.GetExecutingAssembly();
        var stream1 = assembly1.GetManifestResourceStream(RESOURCE_NAME1);
        using var reader1 = new StreamReader(stream1!);
        var result1 = reader1.ReadToEnd();
        Data.Instance.PackageType = string.IsNullOrWhiteSpace(result1) ? "portable" : result1.Trim().ToLowerInvariant();

        Helper.ClearFolder(ConfigPath.TempFolderPath);
        App.Method.SaveConfig();

        Data.UiProperty.ConfigLoaded = true;
        ConfigIdentifyExtension.MinecraftFolder(Data.ConfigEntry);

        InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);

        InitializationEvents.RaiseBeforeUiLoaded();
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Portal.Classes.Entries;
using Portal.Const;
using Portal.Core.Minecraft.Instance;
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

        if (!File.Exists(ConfigPath.SettingDataPath))
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
            Data.ConfigEntry = new ConfigEntry();
        }

        if (FailedSettingKeys.Count > 0) Logger.Error($"Setting load with errors: {FailedSettingKeys.AsJson()}");

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
        Data.Instance.PackageType = string.IsNullOrEmpty(result1) ? "source-code" : result1;

        Helper.ClearFolder(ConfigPath.TempFolderPath);
        App.Method.SaveConfig();

        Data.UiProperty.ConfigLoaded = true;
        ConfigIdentifyExtension.MinecraftFolder(Data.ConfigEntry);

        InstanceManager.Instance.RefreshAll(
            Data.ConfigEntry.MinecraftFolders.Select(f => (f.FolderPath, f.FolderName))
        );

        InitializationEvents.RaiseBeforeUiLoaded();
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Portal.Classes.Entries;
using Portal.Const;
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
            Action = "local",
            Version = "0.0.0",
            BuildTime = DateTime.Now, 
            Commit = "000000",
            Type = "dev"
        };
        Data.UiProperty.OverrideUpdateChannel = Data.Instance.Version.Type;

        Helper.ClearFolder(ConfigPath.TempFolderPath);
        App.Method.SaveConfig();

        Data.UiProperty.ConfigLoaded = true;
        ConfigIdentifyExtension.MinecraftFolder(Data.ConfigEntry);

        InitializationEvents.RaiseBeforeUiLoaded();
    }
}
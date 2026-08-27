using System.Reflection;
using System.Text.Json;
using Portal.Core.Classes;
using Portal.Core.Classes.Config;
using Portal.Core.Classes.Entries;
using Portal.Core.Const;
using Portal.Core.Json;
using Portal.Core.Minecraft.Instance.Bedrock;
using Portal.Core.Services;
using Portal.Localization;
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
        Logger.Info(LogLanguageManager.Instance.config_loadStart.CurrentValue());
        Helper.TryCreateFolder(ConfigPath.UserDataRootPath);
        Helper.TryCreateFolder(ConfigPath.TempFolderPath);
        Helper.TryCreateFolder(ConfigPath.UpdateFolderPath);
        BedrockDataPathResolver.EnsurePortalDataDirectories();

        var isFirstRun = !File.Exists(ConfigPath.SettingDataPath);
        if (isFirstRun)
        {
            Logger.Info(LogLanguageManager.Instance.config_notFoundCreateDefault.CurrentValue());
            File.WriteAllText(ConfigPath.SettingDataPath, new ConfigEntry().AsJson());
        }

        Logger.Info(string.Format(LogLanguageManager.Instance.config_folder.CurrentValue(), ConfigPath.UserDataRootPath));

        InitializationEvents.RaiseBeforeReadSettings();

        try
        {
            var configText = File.ReadAllText(ConfigPath.SettingDataPath);
            Data.ConfigEntry = string.IsNullOrWhiteSpace(configText)
                ? new ConfigEntry()
                : JsonSerializer.Deserialize<ConfigEntry>(configText, PortalJson.Options) ?? new ConfigEntry();

            // Migrate the former shared resource mirror mode to both platform settings.
            if (!string.IsNullOrWhiteSpace(configText))
            {
                using var document = JsonDocument.Parse(configText);
                var root = document.RootElement;
                if (!root.TryGetProperty("ModrinthResourceDownloadSource", out _) &&
                    !root.TryGetProperty("CurseForgeResourceDownloadSource", out _))
                {
                    Data.ConfigEntry.ModrinthResourceDownloadSource = Data.ConfigEntry.ResourceDownloadSource;
                    Data.ConfigEntry.CurseForgeResourceDownloadSource = Data.ConfigEntry.ResourceDownloadSource;
                }
            }
        }
        catch (JsonException exception)
        {
            FailedSettingKeys.Add($"Setting load failed at: {exception.Path}");
            Logger.Error(string.Format(LogLanguageManager.Instance.config_parseFailed.CurrentValue(), ConfigPath.SettingDataPath), exception);
            try
            {
                var backupPath = ConfigPath.SettingDataPath + ".bak";
                File.Copy(ConfigPath.SettingDataPath, backupPath, true);
                Logger.Error(string.Format(LogLanguageManager.Instance.config_backupFailed.CurrentValue(), backupPath));
            }
            catch (Exception backupEx)
            {
                Logger.Error(string.Format(LogLanguageManager.Instance.config_backupFailedError.CurrentValue(), ConfigPath.SettingDataPath), backupEx);
            }

            Data.ConfigEntry = new ConfigEntry();
        }

        Logger.MinimumLevel = Data.ConfigEntry.MinimumLogLevel;
        Data.ConfigEntry.Shortcuts ??= new ShortcutConfig();
        Data.ConfigEntry.JavaVersionDefaultPaths ??= new();

        if (!Enum.IsDefined(typeof(UpdateSource), Data.ConfigEntry.UpdateSource))
        {
            Logger.Info(string.Format(LogLanguageManager.Instance.config_updateSourceInvalid.CurrentValue(), Data.ConfigEntry.UpdateSource));
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
        Logger.Info(string.Format(LogLanguageManager.Instance.config_versionLoaded.CurrentValue(), version.VersionTitle, version.Type));
        Data.UiProperty.OverrideUpdateChannel = Data.ConfigEntry.UpdateSource == UpdateSource.Github
            ? version.Type
            : "release";

        const string RESOURCE_NAME1 = "Portal.Core.Assets.package-type.txt";
        var assembly1 = Assembly.GetExecutingAssembly();
        var stream1 = assembly1.GetManifestResourceStream(RESOURCE_NAME1);
        using var reader1 = new StreamReader(stream1!);
        var result1 = reader1.ReadToEnd();
        Data.Instance.PackageType = string.IsNullOrWhiteSpace(result1) ? "portable" : result1.Trim().ToLowerInvariant();
        Logger.Info(string.Format(LogLanguageManager.Instance.config_packageTypeDetected.CurrentValue(), Data.Instance.PackageType));

        ConfigIdentifyExtension.Window(Data.ConfigEntry);

        ConfigSaver.SaveConfig();

        Data.UiProperty.ConfigLoaded = true;
        ConfigIdentifyExtension.MinecraftFolder(Data.ConfigEntry);

        Logger.Info(string.Format(LogLanguageManager.Instance.config_loadComplete.CurrentValue(), Data.ConfigEntry.MinecraftFolders.Count));

        InitializationEvents.RaiseBeforeUiLoaded();
    }
}

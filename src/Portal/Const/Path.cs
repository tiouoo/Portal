using System;
using System.IO;

namespace Portal.Const;

public static class ConfigPath
{
    private static readonly string SessionTimestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

    public static string UserDataRootPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "xyz.tiouo.Portal");

    public static string TempFolderPath => Path.Combine(UserDataRootPath, "Temp");
    public static string LogFolderPath => Path.Combine(UserDataRootPath, "Log");
    public static string CacheFolderPath => Path.Combine(UserDataRootPath, "Cache");
    public static string UpdateFolderPath => Path.Combine(UserDataRootPath, "Updates");
    public static string BedrockDataRootPath => Path.Combine(UserDataRootPath, "Bedrock");

    public static string SettingDataPath => Path.Combine(UserDataRootPath, "Setting.portal");
    public static string AppPathDataPath => Path.Combine(UserDataRootPath, "AppPath.portal");
}

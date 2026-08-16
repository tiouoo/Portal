using System.Diagnostics;
using Avalonia.Threading;
using Newtonsoft.Json;
using Portal.Core.Classes;
using Portal.Core.Const;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Events;
using Tio.Avalonia.Standard.Modules.Helper;

namespace Portal.Core.Module.Initialize;

public static class ConfigSaver
{
    private static readonly Lock SaveLock = new();

    private static readonly Debouncer Debouncer = new(FlushConfig, 300);

    public static void SaveConfig()
    {
        Debouncer.Invoke();
    }

    public static void FlushConfig()
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.Info($"开始保存应用配置：{ConfigPath.SettingDataPath}");
        try
        {
            var payload = Dispatcher.UIThread.CheckAccess()
                ? CaptureConfig()
                : Dispatcher.UIThread.InvokeAsync(CaptureConfig).GetAwaiter().GetResult();
            lock (SaveLock)
            {
                WriteAtomic(ConfigPath.SettingDataPath, payload.ConfigJson);
                WriteAtomic(Path.Combine(ConfigPath.UserDataRootPath, "ManagedSystemDialogs.portal"),
                    payload.ManagedDialogs);
                WriteAtomic(ConfigPath.DebugConsoleDataPath, payload.DebugConsole);
            }
            Logger.Info($"应用配置保存完成，耗时 {stopwatch.ElapsedMilliseconds} ms。");
        }
        catch (Exception ex)
        {
            Logger.Error($"保存应用配置失败：{ConfigPath.SettingDataPath}", ex);
        }
    }

    private static (string ConfigJson, string ManagedDialogs, string DebugConsole) CaptureConfig()
    {
        ApplicationEvents.RaiseSaveSettings();
        var settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            Formatting = Formatting.Indented
        };
        return (JsonConvert.SerializeObject(Data.ConfigEntry, settings),
            Data.ConfigEntry.FilePicker == FilePicker.Managed ? "true" : "false",
            Data.ConfigEntry.EnableDebugConsole ? "true" : "false");
    }
    private static void WriteAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, true);
    }
}

using System.Diagnostics;
using System.Text.Json;
using Avalonia.Threading;
using Portal.Core.Classes;
using Portal.Core.Const;
using Portal.Core.Json;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Events;
using Tio.Avalonia.Standard.Modules.Helper;

namespace Portal.Core.Module.Initialize;

public static class ConfigSaver
{
    private static readonly Lock SaveLock = new();

    private static readonly Debouncer Debouncer = new(async () => await FlushConfigAsync(), 300);

    public static void SaveConfig()
    {
        Debouncer.Invoke();
    }

    public static void FlushConfig()
    {
        FlushConfigAsync().GetAwaiter().GetResult();
    }

    private static async Task FlushConfigAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.Info(string.Format(LogLanguageManager.Instance.config_saveStart.CurrentValue(), ConfigPath.SettingDataPath));
        try
        {
            var payload = Dispatcher.UIThread.CheckAccess()
                ? CaptureConfig()
                : await Dispatcher.UIThread.InvokeAsync(CaptureConfig);
            await Task.Run(() =>
            {
                lock (SaveLock)
                {
                    WriteAtomic(ConfigPath.SettingDataPath, payload.ConfigJson);
                    WriteAtomic(Path.Combine(ConfigPath.UserDataRootPath, "ManagedSystemDialogs.portal"),
                        payload.ManagedDialogs);
                    WriteAtomic(ConfigPath.DebugConsoleDataPath, payload.DebugConsole);
                }
            }).ConfigureAwait(false);

            Logger.Info(string.Format(LogLanguageManager.Instance.config_saveComplete.CurrentValue(), stopwatch.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            Logger.Error(string.Format(LogLanguageManager.Instance.config_saveFailed.CurrentValue(), ConfigPath.SettingDataPath), ex);
        }
    }

    private static (string ConfigJson, string ManagedDialogs, string DebugConsole) CaptureConfig()
    {
        ApplicationEvents.RaiseSaveSettings();
        return (JsonSerializer.Serialize(Data.ConfigEntry, PortalJson.Options),
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
using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Newtonsoft.Json;
using Portal.Const;
using Tio.Avalonia.Standard.Modules.Events;
using Tio.Avalonia.Standard.Modules.Helper;

namespace Portal;

public partial class App : Application
{
    public static class Method
    {
        private static readonly Debouncer Debouncer = new(() =>
        {
            ApplicationEvents.RaiseSaveSettings();
            File.WriteAllText(ConfigPath.SettingDataPath,
                JsonConvert.SerializeObject(Data.ConfigEntry, Formatting.Indented));
        }, 300);

        public static void SaveConfig()
        {
            Debouncer.Invoke();
        }

        public static async void RestartApp(bool isAdmin = false)
        {
            if (!await ApplicationEvents.RaiseAppExiting()) return;
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory,
                FileName = Process.GetCurrentProcess().MainModule.FileName
            };
            if (isAdmin) startInfo.Verb = "runas";
            Process.Start(startInfo);
            Environment.Exit(0);
        }

        public static async void TryExitApp()
        {
            if (!await ApplicationEvents.RaiseAppExiting()) return;
            Environment.Exit(0);
        }
    }
}
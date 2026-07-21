using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Newtonsoft.Json;
using Portal.Classes.Enums;
using Portal.Const;
using Tio.Avalonia.Standard.Modules.Events;
using Tio.Avalonia.Standard.Modules.Helper;
using TioUi.Shared;

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
            File.WriteAllText(Path.Combine(ConfigPath.UserDataRootPath, "ManagedSystemDialogs.portal"),
                Data.ConfigEntry.FilePicker == FilePicker.Managed ? "true" : "false");
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

    private void ThemeMirage_OnClick(object? sender, EventArgs e)
    {
        Data.ConfigEntry.Theme = Theme.Mirage;
    }

    private void ThemeDark_OnClick(object? sender, EventArgs e)
    {
        Data.ConfigEntry.Theme = Theme.Dark;
    }

    private void ThemeLight_OnClick(object? sender, EventArgs e)
    {
        Data.ConfigEntry.Theme = Theme.Light;
    }

    private void ThemeDefault_OnClick(object? sender, EventArgs e)
    {
        Data.ConfigEntry.Theme = Theme.System;
    }
}
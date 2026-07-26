using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Threading;
using Newtonsoft.Json;
using Portal.Classes.Enums;
using Portal.Const;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Events;
using Tio.Avalonia.Standard.Modules.Helper;
using TioUi.Shared;

namespace Portal;

public partial class App : Application
{
    public static class Method
    {
        private static readonly object SaveLock = new();

        private static readonly Debouncer Debouncer = new(FlushConfig, 300);

        public static void SaveConfig()
        {
            Debouncer.Invoke();
        }

        /// <summary>
        /// 立即同步保存配置。序列化涉及 UI 线程上的集合，因此固定在 UI 线程执行；
        /// 从其他线程调用时阻塞等待 UI 线程完成序列化（在 UI 线程上调用则直接执行，不会死锁）。
        /// 退出前必须调用，避免防抖中的保存丢失。
        /// </summary>
        public static void FlushConfig()
        {
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
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"保存配置失败：{ex}");
            }
        }

        private static (string ConfigJson, string ManagedDialogs) CaptureConfig()
        {
            ApplicationEvents.RaiseSaveSettings();
            return (JsonConvert.SerializeObject(Data.ConfigEntry, Formatting.Indented),
                Data.ConfigEntry.FilePicker == FilePicker.Managed ? "true" : "false");
        }

        /// <summary>先写同目录临时文件再原子替换，避免写入中途崩溃损坏原文件。</summary>
        private static void WriteAtomic(string path, string content)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, content);
            File.Move(temp, path, true);
        }

        public static async void RestartApp(bool isAdmin = false)
        {
            if (!await ApplicationEvents.RaiseAppExiting()) return;
            FlushConfig();
            // AppImage 的可执行文件路径指向临时挂载点，退出后即失效；优先使用 APPIMAGE 环境变量给出的包文件路径
            var fileName = Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImagePath &&
                           File.Exists(appImagePath)
                ? appImagePath
                : Process.GetCurrentProcess().MainModule.FileName;
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory,
                FileName = fileName
            };
            if (isAdmin) startInfo.Verb = "runas";
            Process.Start(startInfo);
            Environment.Exit(0);
        }

        public static async void TryExitApp()
        {
            if (!await ApplicationEvents.RaiseAppExiting()) return;
            FlushConfig();
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
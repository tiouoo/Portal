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
            // Logger.Debug("已请求保存应用配置（防抖）。");
            Debouncer.Invoke();
        }

        /// <summary>
        /// 立即同步保存配置。序列化涉及 UI 线程上的集合，因此固定在 UI 线程执行；
        /// 从其他线程调用时阻塞等待 UI 线程完成序列化（在 UI 线程上调用则直接执行，不会死锁）。
        /// 退出前必须调用，避免防抖中的保存丢失。
        /// </summary>
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
            // 与读取配置保持一致：使用 TypeNameHandling.Auto 写出 WidgetLayoutData.Data 的 $type，
            // 否则下次启动会反序列化为 JObject 而丢失组件自定义数据。
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                Formatting = Formatting.Indented
            };
            return (JsonConvert.SerializeObject(Data.ConfigEntry, settings),
                Data.ConfigEntry.FilePicker == FilePicker.Managed ? "true" : "false",
                Data.ConfigEntry.EnableDebugConsole ? "true" : "false");
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

using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using Portal.Bedrock.Standard.Interface;
using Portal.Const;
using Portal.Core.Const;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.ToolsPages;

public partial class BedrockToolsPage : UserControl
{
    private const string WindowsAppSdk18InstallerUrl =
        "https://aka.ms/windowsappsdk/1.8/1.8.260710003/windowsappruntimeinstall-x64.exe";

    public BedrockToolsPage()
    {
        InitializeComponent();
        DataContext = this;
    }

    public bool IsWindows => OperatingSystem.IsWindows();

    private async void DownloadFramework_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } topLevel)
            await topLevel.Launcher.LaunchUriAsync(new Uri("https://www.mcappx.com/download/mc-framework/"));
    }

    private async void CheckSdk_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;
        if (BedrockToolsService.Default is null)
        {
            NotificationGateway.Notice(topLevel, "SDK 1.8 检测仅支持 Windows", NotificationType.Warning);
            return;
        }

        try
        {
            var installed = await BedrockToolsService.Default.IsWindowsAppSdk18InstalledAsync();
            if (installed)
            {
                this.GetTopLevel().Notice("当前系统已安装完整的 Windows App SDK 1.8，无需再次安装", NotificationType.Success);
                return;
            }

            var result = await OverlayDialog.ShowStandardAsync(new TextBlock
            {
                Margin = new Avalonia.Thickness(24),
                Text = "当前系统未检测到完整的 Windows App SDK 1.8 (8000.x)。\n缺少 Main、Singleton 或 DDLM 组件可能导致游戏无法启动，是否立即下载安装？",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            }, null, this.TryGetHostId(), new OverlayDialogOptions
            {
                Title = "未安装 SDK 1.8",
                Mode = DialogMode.Warning,
                Buttons = DialogButton.YesNo,
                OverrideYesButtonText = "安装",
                OverrideNoButtonText = "取消",
                CanLightDismiss = false,
                CanResize = false
            });
            if (result == DialogResult.Yes) StartSdkInstall(topLevel);
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            NotificationGateway.Notice(topLevel, "SDK 1.8 检测失败", NotificationType.Error);
        }
    }

    private static void StartSdkInstall(TopLevel topLevel)
    {
        var installerPath = Path.Combine(ConfigPath.TempFolderPath,
            $"windows-app-sdk-1.8-{Guid.NewGuid():N}.exe");
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = "安装 Windows App SDK 1.8",
            Description = "正在准备下载",
            Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = "取消安装",
                    Description = "取消 SDK 1.8 下载和安装",
                    IconKey = "Cancel",
                    ExecuteAsync = (managedTask, _) =>
                    {
                        managedTask.RequestCancellation();
                        return Task.CompletedTask;
                    },
                    CanExecute = managedTask => managedTask.CanBeCancelled,
                    IsVisible = managedTask => !managedTask.IsTerminal
                }
            ]
        }, async context =>
        {
            Directory.CreateDirectory(ConfigPath.TempFolderPath);
            try
            {
                context.SetRunning("正在下载 Windows App SDK 1.8");
                var request = new DownloadRequest(WindowsAppSdk18InstallerUrl, installerPath)
                {
                    ProgressChanged = progress => Dispatcher.UIThread.Post(() =>
                    {
                        if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                        context.ReportProgress(progress.TotalBytes > 0
                            ? Math.Clamp((double)progress.DownloadedBytes / progress.TotalBytes, 0, 1)
                            : null);
                        context.SetDescription($"正在下载，速度：{DefaultDownloader.FormatSize(progress.Speed, true)}");
                    })
                };
                var downloader = new DefaultDownloader
                {
                    MaxFragment = Math.Max(1, Data.ConfigEntry.CustomDownloadMaxFragmentCount),
                    IsEnableFragment = true
                };
                var download = await downloader.DownloadAsync(request, context.CancellationToken);
                if (download.Type == DownloadResultType.Cancelled)
                    throw new OperationCanceledException(context.CancellationToken);
                if (download.Type != DownloadResultType.Successful)
                    throw download.Exception ?? new IOException("Windows App SDK 1.8 下载失败。");

                context.SetDescription("等待管理员权限并安装");
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true,
                    Verb = "runas"
                }) ?? throw new InvalidOperationException("无法启动 Windows App SDK 安装程序。");
                await process.WaitForExitAsync(context.CancellationToken);
                if (process.ExitCode is not (0 or 3010))
                    throw new InvalidOperationException($"Windows App SDK 安装程序退出码：{process.ExitCode}。");
                context.ReportProgress(1);
                context.SetDescription(process.ExitCode == 3010 ? "安装完成，重启系统后生效" : "安装完成");
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode is 1223 or 5)
            {
                throw new OperationCanceledException("用户取消了管理员权限授权。", exception,
                    context.CancellationToken);
            }
            finally
            {
                try
                {
                    if (File.Exists(installerPath)) File.Delete(installerPath);
                }
                catch (IOException exception)
                {
                    Logger.Warning($"[BedrockTools] 无法删除 SDK 安装程序：{exception}");
                }
            }
        });
        task.Start();
        _ = ObserveSdkInstallAsync(task, topLevel);
    }

    private static async Task ObserveSdkInstallAsync(ManagedTask task, TopLevel topLevel)
    {
        try
        {
            await task.Completion;
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[BedrockTools] SDK 1.8 安装已取消：{exception.Message}");
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (task.Status == ManagedTaskStatus.Completed)
                NotificationGateway.Notice(topLevel, "Windows App SDK 1.8 安装完成", NotificationType.Success);
            else if (task.Status == ManagedTaskStatus.Faulted)
                NotificationGateway.Notice(topLevel, "Windows App SDK 1.8 安装失败", NotificationType.Error);
        });
    }

    private async void UninstallMinecraft_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;
        if (BedrockToolsService.Default is null)
        {
            NotificationGateway.Notice(topLevel, "卸载 Microsoft Store Minecraft 仅支持 Windows", NotificationType.Warning);
            return;
        }

        var result = await OverlayDialog.ShowStandardAsync(new TextBlock
        {
            Margin = new Avalonia.Thickness(24),
            Text = "确定要卸载本机从 Microsoft Store 安装的 Minecraft 基岩版吗？\n此操作不会删除 Portal 管理的独立实例。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        }, null, this.TryGetHostId(), new OverlayDialogOptions
        {
            Title = "卸载 Minecraft",
            Mode = DialogMode.Error,
            Buttons = DialogButton.YesNo,
            OverrideYesButtonText = "卸载",
            OverrideNoButtonText = "取消",
            CanLightDismiss = false,
            CanResize = false
        });
        if (result != DialogResult.Yes) return;

        try
        {
            await BedrockToolsService.Default.UninstallMinecraftAsync();
            NotificationGateway.Notice(topLevel, "Minecraft 卸载完成", NotificationType.Success);
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            NotificationGateway.Notice(topLevel, "Minecraft 卸载失败", NotificationType.Error);
        }
    }
}

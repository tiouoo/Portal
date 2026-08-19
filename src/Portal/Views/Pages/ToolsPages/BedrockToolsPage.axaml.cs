using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using Portal.Bedrock.Standard.Interface;
using Portal.Core.Const;
using Portal.Localization;
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
            topLevel.Notice(CommonLanguageManager.Instance.bedrockTools_sdkWindowsOnly.CurrentValue(),
                NotificationType.Warning);
            return;
        }

        try
        {
            var installed = await BedrockToolsService.Default.IsWindowsAppSdk18InstalledAsync();
            if (installed)
            {
                this.GetTopLevel().Notice(CommonLanguageManager.Instance.bedrockTools_sdkInstalled.CurrentValue(),
                    NotificationType.Success);
                return;
            }

            var result = await OverlayDialog.ShowStandardAsync(new TextBlock
            {
                Margin = new Thickness(24),
                Text = CommonLanguageManager.Instance.bedrockTools_sdkMissingText.CurrentValue(),
                TextWrapping = TextWrapping.Wrap
            }, null, this.TryGetHostId(), new OverlayDialogOptions
            {
                Title = CommonLanguageManager.Instance.bedrockTools_sdkMissingTitle.CurrentValue(),
                Mode = DialogMode.Warning,
                Buttons = DialogButton.YesNo,
                OverrideYesButtonText = CommonLanguageManager.Instance.bedrockTools_install.CurrentValue(),
                OverrideNoButtonText = CommonLanguageManager.Instance.common_cancel.CurrentValue(),
                CanLightDismiss = false,
                CanResize = false
            });
            if (result == DialogResult.Yes) StartSdkInstall(topLevel);
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            topLevel.Notice(CommonLanguageManager.Instance.bedrockTools_sdkCheckFailed.CurrentValue(),
                NotificationType.Error);
        }
    }

    private static void StartSdkInstall(TopLevel topLevel)
    {
        var installerPath = Path.Combine(ConfigPath.TempFolderPath,
            $"windows-app-sdk-1.8-{Guid.NewGuid():N}.exe");
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = CommonLanguageManager.Instance.bedrockTools_installSdkTaskName.CurrentValue(),
            Description = CommonLanguageManager.Instance.bedrockTools_preparingDownload.CurrentValue(),
            Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = CommonLanguageManager.Instance.minecraft_cancelInstall.CurrentValue(),
                    Description = CommonLanguageManager.Instance.bedrockTools_cancelInstallDescription.CurrentValue(),
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
                context.SetRunning(CommonLanguageManager.Instance.bedrockTools_downloadingSdk.CurrentValue());
                var request = new DownloadRequest(WindowsAppSdk18InstallerUrl, installerPath)
                {
                    ProgressChanged = progress => Dispatcher.UIThread.Post(() =>
                    {
                        if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                        context.ReportProgress(progress.TotalBytes > 0
                            ? Math.Clamp((double)progress.DownloadedBytes / progress.TotalBytes, 0, 1)
                            : null);
                        context.SetDescription(string.Format(
                            CommonLanguageManager.Instance.bedrockTools_downloadingSpeed.CurrentValue(),
                            DefaultDownloader.FormatSize(progress.Speed, true)));
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
                    throw download.Exception ??
                          new IOException(CommonLanguageManager.Instance.bedrockTools_downloadFailed.CurrentValue());

                context.SetDescription(CommonLanguageManager.Instance.bedrockTools_waitingAdmin.CurrentValue());
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true,
                    Verb = "runas"
                }) ?? throw new InvalidOperationException(
                    CommonLanguageManager.Instance.bedrockTools_cannotStartInstaller.CurrentValue());
                await process.WaitForExitAsync(context.CancellationToken);
                if (process.ExitCode is not (0 or 3010))
                    throw new InvalidOperationException(string.Format(
                        CommonLanguageManager.Instance.bedrockTools_installerExitCode.CurrentValue(),
                        process.ExitCode));
                context.ReportProgress(1);
                context.SetDescription(process.ExitCode == 3010
                    ? CommonLanguageManager.Instance.bedrockTools_installCompleteRestart.CurrentValue()
                    : CommonLanguageManager.Instance.bedrockTools_installComplete.CurrentValue());
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode is 1223 or 5)
            {
                throw new OperationCanceledException(
                    CommonLanguageManager.Instance.bedrockTools_adminCancelled.CurrentValue(), exception,
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
                    Logger.Warning(string.Format(
                        LogLanguageManager.Instance.bedrockTools_deleteInstallerFailed.CurrentValue(), exception));
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
            Logger.Debug(string.Format(
                LogLanguageManager.Instance.bedrockTools_installCancelled.CurrentValue(), exception.Message));
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (task.Status == ManagedTaskStatus.Completed)
                topLevel.Notice(CommonLanguageManager.Instance.bedrockTools_sdkInstallComplete.CurrentValue(),
                    NotificationType.Success);
            else if (task.Status == ManagedTaskStatus.Faulted)
                topLevel.Notice(CommonLanguageManager.Instance.bedrockTools_sdkInstallFailed.CurrentValue(),
                    NotificationType.Error);
        });
    }

    private async void UninstallMinecraft_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;
        if (BedrockToolsService.Default is null)
        {
            topLevel.Notice(CommonLanguageManager.Instance.bedrockTools_uninstallWindowsOnly.CurrentValue(),
                NotificationType.Warning);
            return;
        }

        var result = await OverlayDialog.ShowStandardAsync(new TextBlock
        {
            Margin = new Thickness(24),
            Text = CommonLanguageManager.Instance.bedrockTools_uninstallConfirm.CurrentValue(),
            TextWrapping = TextWrapping.Wrap
        }, null, this.TryGetHostId(), new OverlayDialogOptions
        {
            Title = CommonLanguageManager.Instance.bedrockTools_uninstallTitle.CurrentValue(),
            Mode = DialogMode.Error,
            Buttons = DialogButton.YesNo,
            OverrideYesButtonText = CommonLanguageManager.Instance.bedrockTools_uninstall.CurrentValue(),
            OverrideNoButtonText = CommonLanguageManager.Instance.common_cancel.CurrentValue(),
            CanLightDismiss = false,
            CanResize = false
        });
        if (result != DialogResult.Yes) return;

        try
        {
            await BedrockToolsService.Default.UninstallMinecraftAsync();
            topLevel.Notice(CommonLanguageManager.Instance.bedrockTools_uninstallComplete.CurrentValue(),
                NotificationType.Success);
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            topLevel.Notice(CommonLanguageManager.Instance.bedrockTools_uninstallFailed.CurrentValue(),
                NotificationType.Error);
        }
    }
}
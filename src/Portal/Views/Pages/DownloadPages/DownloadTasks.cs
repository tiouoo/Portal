using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using MinecraftLaunch.Base.EventArgs;
using MinecraftLaunch.Components.Downloader;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;

namespace Portal.Views.Pages.DownloadPages;

internal static class DownloadProgressReporter
{
    /// <summary>MinecraftLaunch 下载进度 —— 仅供仍使用 ML 下载器的场景（如整合包安装）。</summary>
    public static Action<ResourceDownloadProgressChangedEventArgs> Create(TaskExecutionContext context,
        Func<double, string>? formatSpeed = null)
    {
        ResourceDownloadProgressChangedEventArgs? latestProgress = null;
        var dispatchQueued = 0;
        return progress =>
        {
            if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;

            Volatile.Write(ref latestProgress, progress);
            if (Interlocked.Exchange(ref dispatchQueued, 1) != 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref dispatchQueued, 0);
                if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                if (Volatile.Read(ref latestProgress) is { } current)
                {
                    context.ReportProgress(current.TotalBytes > 0
                        ? Math.Clamp((double)current.DownloadedBytes / current.TotalBytes, 0, 1)
                        : null);
                    context.SetDescription(formatSpeed is not null
                        ? formatSpeed(current.Speed)
                        : string.Format(CommonLanguageManager.Instance.download_speed.CurrentValue(),
                            DefaultDownloader.FormatSize(current.Speed, true)));
                }
            }, DispatcherPriority.Background);
        };
    }

    /// <summary>Iridium 下载进度 —— 按已完成文件计数汇报。</summary>
    public static Action<Iridium.Models.Download.ResourceDownloadProgressChangedEventArgs> CreateIridium(
        TaskExecutionContext context)
    {
        Iridium.Models.Download.ResourceDownloadProgressChangedEventArgs? latestProgress = null;
        var dispatchQueued = 0;
        return progress =>
        {
            if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;

            Volatile.Write(ref latestProgress, progress);
            if (Interlocked.Exchange(ref dispatchQueued, 1) != 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref dispatchQueued, 0);
                if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                if (Volatile.Read(ref latestProgress) is { } current)
                    context.ReportProgress(current.Progress);
            }, DispatcherPriority.Background);
        };
    }
}

internal static class DownloadTasks
{
    public static TaskActionDefinition CreateCancelAction(string description)
    {
        return new TaskActionDefinition
        {
            Name = CommonLanguageManager.Instance.download_cancel.CurrentValue(), Description = description,
            IconKey = "Cancel",
            ExecuteAsync = (managedTask, _) =>
            {
                managedTask.RequestCancellation();
                return Task.CompletedTask;
            },
            CanExecute = managedTask => managedTask.CanBeCancelled,
            IsVisible = managedTask => !managedTask.IsTerminal
        };
    }

    public static ManagedTask Download(TopLevel topLevel, string taskName, string cancelDescription, string fileName,
        string downloadUrl, string destination, long fileSize, Func<TaskExecutionContext, Task>? afterDownload = null,
        string completedText = "", Func<double, string>? formatSpeed = null,
        string failureMessage = "")
    {
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = taskName,
            Description = CommonLanguageManager.Instance.download_connecting.CurrentValue(),
            Progress = 0,
            Actions = [CreateCancelAction(cancelDescription)]
        }, async context =>
        {
            context.SetRunning(string.Format(CommonLanguageManager.Instance.download_downloading.CurrentValue(),
                fileName));
            var request = new Iridium.Models.Download.DownloadRequest
            {
                Url = downloadUrl,
                LocalPath = destination,
                Size = fileSize,
                ProgressChanged = DownloadProgressReporter.CreateIridium(context)
            };
            var result = await new Iridium.Download.DefaultDownloader().DownloadAsync(request,
                context.CancellationToken);
            if (result.SuccessCount > 0)
            {
                if (afterDownload is not null) await afterDownload(context);
                context.ReportProgress(1);
                context.SetDescription(string.IsNullOrEmpty(completedText)
                    ? CommonLanguageManager.Instance.download_complete.CurrentValue()
                    : completedText);
                return;
            }

            if (context.Task.IsCancellationRequested || context.CancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(context.CancellationToken);
            throw result.Exceptions.FirstOrDefault() ?? new IOException(string.IsNullOrEmpty(failureMessage)
                ? CommonLanguageManager.Instance.download_failed.CurrentValue()
                : failureMessage);
        });
        task.Start();
        _ = ObserveAsync(task, topLevel, fileName);
        return task;
    }

    public static async Task ObserveAsync(ManagedTask task, TopLevel topLevel, string fileName)
    {
        try
        {
            await task.Completion;
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[Download] Download {fileName} was cancelled: {exception}");
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
        }

        if (task.Status == ManagedTaskStatus.Completed)
        {
            Logger.Info($"[Download] Download completed for {fileName}.");
            Dispatcher.UIThread.Post(() => topLevel.Notice(string.Format(
                CommonLanguageManager.Instance.download_downloaded.CurrentValue(), fileName),
                NotificationType.Success));
        }
        else if (task.Status == ManagedTaskStatus.Faulted)
        {
            Logger.Warning($"[Download] Download failed for {fileName}: {task.Exception}");
            Dispatcher.UIThread.Post(() => topLevel.Notice(string.Format(
                CommonLanguageManager.Instance.download_downloadFailed.CurrentValue(), fileName),
                NotificationType.Error));
        }

        await Task.Delay(TimeSpan.FromSeconds(3));
        Dispatcher.UIThread.Post(() => TaskManager.Instance.RemoveTerminalTask(task));
    }
}

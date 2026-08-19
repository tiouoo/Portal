using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.EventArgs;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;

namespace Portal.Views.Pages.DownloadPages;

internal static class DownloadProgressReporter
{
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
                        : $"下载速度：{DefaultDownloader.FormatSize(current.Speed, true)}");
                }
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
            Name = "取消下载", Description = description, IconKey = "Cancel",
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
        string completedText = "下载完成", Func<double, string>? formatSpeed = null,
        string failureMessage = "下载失败。")
    {
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = taskName,
            Description = "正在连接下载服务器",
            Progress = 0,
            Actions = [CreateCancelAction(cancelDescription)]
        }, async context =>
        {
            context.SetRunning($"正在下载：{fileName}");
            var request = new DownloadRequest(downloadUrl, destination, fileSize)
            {
                ProgressChanged = DownloadProgressReporter.Create(context, formatSpeed)
            };
            var result = await new DefaultDownloader().DownloadAsync(request, context.CancellationToken);
            if (result.Type == DownloadResultType.Cancelled)
                throw new OperationCanceledException(context.CancellationToken);
            if (result.Type != DownloadResultType.Successful)
                throw result.Exception ?? new IOException(failureMessage);
            if (afterDownload is not null) await afterDownload(context);
            context.ReportProgress(1);
            context.SetDescription(completedText);
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
            Dispatcher.UIThread.Post(() => topLevel.Notice($"{fileName} 下载完成", NotificationType.Success));
        }
        else if (task.Status == ManagedTaskStatus.Faulted)
        {
            Logger.Warning($"[Download] Download failed for {fileName}: {task.Exception}");
            Dispatcher.UIThread.Post(() => topLevel.Notice($"{fileName} 下载失败", NotificationType.Error));
        }

        await Task.Delay(TimeSpan.FromSeconds(3));
        Dispatcher.UIThread.Post(() => TaskManager.Instance.RemoveTerminalTask(task));
    }
}

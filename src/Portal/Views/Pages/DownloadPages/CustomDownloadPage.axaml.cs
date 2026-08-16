using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using Portal.Const;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;

namespace Portal.Views.Pages.DownloadPages;

public partial class CustomDownloadPage : UserControl
{
    public CustomDownloadPage()
    {
        InitializeComponent();
        DataContext = new CustomDownloadPageViewModel();
    }

    private async void Browse_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || DataContext is not CustomDownloadPageViewModel viewModel)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择保存文件夹",
            AllowMultiple = false
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            viewModel.FolderPath = path;
    }

    private void Download_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || DataContext is not CustomDownloadPageViewModel viewModel)
            return;

        if (!Uri.TryCreate(viewModel.Url?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            NotificationGateway.Notice(topLevel, "请输入有效的 HTTP/HTTPS 链接", NotificationType.Warning);
            return;
        }

        var folder = viewModel.FolderPath?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(folder))
        {
            NotificationGateway.Notice(topLevel, "请选择保存文件夹", NotificationType.Warning);
            return;
        }

        var fileName = viewModel.FileName?.Trim();
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = CustomDownloadPageViewModel.GetFileNameFromUrl(viewModel.Url) ?? "download";

        string destination;
        try
        {
            
            fileName = CustomDownloadPageViewModel.DeduplicateFileName(folder, fileName);
            destination = Path.GetFullPath(Path.Combine(folder, fileName));
        }
        catch (Exception exception)
        {
            Logger.Warning($"[CustomDownload] Invalid destination {folder}/{fileName}: {exception}");
            NotificationGateway.Notice(topLevel, "保存文件夹或文件名无效", NotificationType.Warning);
            return;
        }

        viewModel.ApplyAutoFileName(fileName);
        StartDownload(topLevel, uri.AbsoluteUri, destination);
    }

    private static void StartDownload(TopLevel topLevel, string url, string destination)
    {
        var fileName = Path.GetFileName(destination);
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = $"自定义下载：{fileName}",
            Description = "正在连接下载服务器",
            Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = "取消下载", Description = "取消此自定义下载", IconKey = "Cancel",
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
            context.SetRunning($"正在下载：{fileName}");
            var request = new DownloadRequest(url, destination)
            {
                ProgressChanged = progress => Dispatcher.UIThread.Post(() =>
                {
                    if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                    var fraction = progress.TotalBytes > 0
                        ? Math.Clamp((double)progress.DownloadedBytes / progress.TotalBytes, 0, 1)
                        : (double?)null;
                    context.ReportProgress(fraction);
                    context.SetDescription($"下载速度：{DefaultDownloader.FormatSize(progress.Speed, true)}");
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
                throw download.Exception ?? new IOException("文件下载失败。");
            context.ReportProgress(1);
            context.SetDescription("下载完成");
        });
        task.Start();
        _ = ObserveDownloadAsync(task, topLevel, fileName);
    }

    private static async Task ObserveDownloadAsync(ManagedTask task, TopLevel topLevel, string fileName)
    {
        try
        {
            await task.Completion;
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[CustomDownload] Download {fileName} was cancelled: {exception}");
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
        }

        if (task.Status == ManagedTaskStatus.Completed)
            Dispatcher.UIThread.Post(() =>
                NotificationGateway.Notice(topLevel, $"{fileName} 下载完成", NotificationType.Success));
        else if (task.Status == ManagedTaskStatus.Faulted)
            Dispatcher.UIThread.Post(() =>
                NotificationGateway.Notice(topLevel, $"{fileName} 下载失败", NotificationType.Error));
        await Task.Delay(TimeSpan.FromSeconds(3));
        Dispatcher.UIThread.Post(() => TaskManager.Instance.RemoveTerminalTask(task));
    }
}

public partial class CustomDownloadPageViewModel : ObservableObject
{
    private string _lastAutoFileName = string.Empty;

    public Data Data => Data.Instance;

    [ObservableProperty] public partial string Url { get; set; } = string.Empty;
    [ObservableProperty] public partial string FolderPath { get; set; } = string.Empty;
    [ObservableProperty] public partial string FileName { get; set; } = string.Empty;

    partial void OnUrlChanged(string value) => AutoFillFileName();

    partial void OnFolderPathChanged(string value) => AutoFillFileName();

        private void AutoFillFileName()
    {
        if (!string.IsNullOrEmpty(FileName) && FileName != _lastAutoFileName)
            return;

        var name = GetFileNameFromUrl(Url);
        if (name is null)
            return;

        ApplyAutoFileName(DeduplicateFileName(FolderPath, name));
    }

    public void ApplyAutoFileName(string name)
    {
        _lastAutoFileName = name;
        FileName = name;
    }

    public static string? GetFileNameFromUrl(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri))
            return null;

        try
        {
            var name = Path.GetFileName(uri.LocalPath);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception exception)
        {
            Logger.Debug($"[CustomDownload] Could not derive a file name from {url}: {exception}");
            return null;
        }
    }

        public static string DeduplicateFileName(string? folder, string name)
    {
        folder = folder?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(folder))
            return name;

        try
        {
            if (!File.Exists(Path.Combine(folder, name)))
                return name;

            var stem = Path.GetFileNameWithoutExtension(name);
            var extension = Path.GetExtension(name);
            for (var index = 1;; index++)
            {
                var candidate = $"{stem} ({index}){extension}";
                if (!File.Exists(Path.Combine(folder, candidate)))
                    return candidate;
            }
        }
        catch (Exception exception)
        {
            Logger.Warning($"[CustomDownload] Could not deduplicate {name} in {folder}: {exception}");
            return name;
        }
    }
}

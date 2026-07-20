using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using AsyncImageLoader;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Components.Provider;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Services;
using Portal.Views.Pages.InstancePages;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.DownloadPages;

public static class JavaResourceDetailsIcon
{
    public const string Data =
        "F1 M640,640z M0,0z M560.3,301.2C570.7,313 588.6,315.6 602.1,306.7 616.8,296.9 620.8,277 611,262.3L563,190.3C560.2,186.1,556.4,182.6,551.9,180.1L351.4,68.7C332.1,58 308.6,58 289.2,68.7L88.8,180C83.4,183,79.1,187.4,76.2,192.8L27.7,282.7C15.1,306.1,23.9,335.2,47.3,347.8L80.3,365.5 80.3,418.8C80.3,441.8,92.7,463.1,112.7,474.5L288.7,574.2C308.3,585.3,332.2,585.3,351.8,574.2L527.8,474.5C547.9,463.1,560.2,441.9,560.2,418.8L560.2,301.3z M320.3,291.4L170.2,208 320.3,124.6 470.4,208 320.3,291.4z M278.8,341.6L257.5,387.8 91.7,299 117.1,251.8 278.8,341.6z";
}

public sealed record JavaResourceDetailsTarget(JavaResourceDefinition Definition, ModDetailsSource Source,
    string ProjectId, string GameVersion = "", ModLoaderType Loader = ModLoaderType.Any);

public abstract partial class JavaResourceDetailsViewModel(JavaResourceDetailsTarget target) : ObservableObject, IDisposable
{
    private readonly ModrinthProvider _modrinth = new();
    private readonly CurseforgeProvider _curseforge = new();
    private readonly CancellationTokenSource _disposeCancellation = new();
    private bool _loaded;

    public JavaResourceDetailsTarget Target { get; } = target;
    public ObservableCollection<string> Screenshots { get; } = [];
    public ObservableCollection<int> ScreenshotIndices { get; } = [];
    [ObservableProperty] public partial ObservableCollection<JavaResourceVersionGroup> VersionGroups { get; set; } = [];
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string Summary { get; set; } = string.Empty;
    [ObservableProperty] public partial string Metadata { get; set; } = string.Empty;
    [ObservableProperty] public partial string? IconUrl { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool HasError { get; set; }
    [ObservableProperty] public partial int SelectedScreenshotIndex { get; set; }
    public IAsyncImageLoader ImageLoader { get; } = new ModImageLoader();
    public IAsyncImageLoader ScreenshotLoader { get; } = new ModScreenshotLoader();
    public string LoadingText => $"正在加载{Target.Definition.DisplayName}详情与所有版本...";
    public string ErrorText => $"无法加载{Target.Definition.DisplayName}详情，请检查网络";
    public bool HasScreenshots => Screenshots.Count > 0;
    public bool HasVersions => VersionGroups.Count > 0;
    public bool IsEmpty => !IsLoading && !HasError && VersionGroups.Count == 0;
    public bool SupportsDownload => Target.Definition.SupportsDownload;
    private IReadOnlyList<JavaResourceFileItem> AllFiles { get; set; } = [];

    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        IsLoading = true;
        try
        {
            var cancellationToken = _disposeCancellation.Token;
            if (Target.Source == ModDetailsSource.Modrinth)
            {
                var project = await _modrinth.SearchByProjectIdAsync(Target.ProjectId, cancellationToken);
                Name = project.Name;
                Summary = project.Summary;
                IconUrl = project.IconUrl;
                Metadata = $"Modrinth·{JavaResourceSearchResultItem.FormatRelativeTime(project.Updated)}·{project.DownloadCount:N0} 下载";
                AddScreenshots(project.Screenshots);
                AllFiles = (await _modrinth.GetModFilesByProjectIdAsync(Target.ProjectId, cancellationToken))
                    .Select(JavaResourceFileItem.From).ToArray();
            }
            else
            {
                var project = (await _curseforge.GetResourcesByModIdsAsync([long.Parse(Target.ProjectId)],
                    cancellationToken)).First();
                Name = project.Name;
                Summary = project.Summary;
                IconUrl = project.IconUrl;
                Metadata = $"CurseForge·{JavaResourceSearchResultItem.FormatRelativeTime(project.DateModified)}·{project.DownloadCount:N0} 下载";
                AddScreenshots(project.Screenshots);
                AllFiles = (await _curseforge.GetModFilesAsync(project.Id, cancellationToken))
                    .Select(JavaResourceFileItem.From).ToArray();
            }
            BuildVersionGroups();
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
        }
        catch
        {
            HasError = true;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private void BuildVersionGroups()
    {
        var groups = AllFiles.SelectMany(file => file.MinecraftVersions.DefaultIfEmpty("未知版本")
                .Select(version => (Version: version, File: file)))
            .GroupBy(item => item.Version)
            .OrderByDescending(group => MinecraftVersionKey.Parse(group.Key))
            .Select(group => new JavaResourceVersionGroup(group.Key,
                group.Select(item => item.File).DistinctBy(file => file.Id).OrderByDescending(file => file.Published)
                    .ToArray()))
            .ToArray();
        VersionGroups = new ObservableCollection<JavaResourceVersionGroup>(groups);
        var targetGroup = VersionGroups.FirstOrDefault(group => group.MinecraftVersion == Target.GameVersion) ??
                          VersionGroups.FirstOrDefault();
        if (targetGroup is not null) targetGroup.IsExpanded = true;
        OnPropertyChanged(nameof(HasVersions));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void AddScreenshots(IEnumerable<string>? urls)
    {
        if (urls is null) return;
        foreach (var url in urls.Where(url => !string.IsNullOrWhiteSpace(url)).Distinct())
        {
            Screenshots.Add(url);
            ScreenshotIndices.Add(ScreenshotIndices.Count);
        }
        OnPropertyChanged(nameof(HasScreenshots));
    }

    public void Dispose()
    {
        _disposeCancellation.Cancel();
        _disposeCancellation.Dispose();
        VersionGroups = [];
    }
}

public sealed partial class JavaResourceVersionGroup : ObservableObject
{
    private const int PageSize = 20;
    private readonly IReadOnlyList<JavaResourceFileItem> _files;
    public string Title { get; }
    public string MinecraftVersion { get; }
    public ObservableCollection<JavaResourceFileItem> VisibleFiles { get; } = [];
    public string FileCountText => $"{_files.Count} 个文件";
    public bool HasMore => VisibleFiles.Count < _files.Count;
    public string LoadMoreText => $"显示更多（剩余 {_files.Count - VisibleFiles.Count} 个）";
    [ObservableProperty] public partial bool IsExpanded { get; set; }

    public JavaResourceVersionGroup(string minecraftVersion, IReadOnlyList<JavaResourceFileItem> files)
    {
        Title = minecraftVersion;
        MinecraftVersion = minecraftVersion;
        _files = files;
        LoadMore();
    }

    [RelayCommand]
    private void LoadMore()
    {
        foreach (var file in _files.Skip(VisibleFiles.Count).Take(PageSize)) VisibleFiles.Add(file);
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(LoadMoreText));
    }
}

public sealed record JavaResourceFileItem(string Id, string DisplayName, string Details, string FileName,
    string DownloadUrl, long FileSize, DateTime Published, IReadOnlyList<string> MinecraftVersions)
{
    public static JavaResourceFileItem From(ModrinthResourceFile file) => new(file.VersionId,
        string.IsNullOrWhiteSpace(file.DisplayName) ? file.FileName : file.DisplayName,
        FormatDetails(file.FileName, file.Published, file.ReleaseType), file.FileName, file.DownloadUrl,
        file.FileSize, file.Published, file.MinecraftVersions.ToArray());

    public static JavaResourceFileItem From(CurseforgeResourceFile file) => new(file.Id.ToString(),
        string.IsNullOrWhiteSpace(file.DisplayName) ? file.FileName : file.DisplayName,
        FormatDetails(file.FileName, file.Published, file.ReleaseType), file.FileName, file.DownloadUrl,
        file.FileLength, file.Published, file.GameVersions.Where(IsMinecraftVersion).ToArray());

    private static string FormatDetails(string fileName, DateTime published, FileReleaseType releaseType) =>
        $"{fileName}·{JavaResourceSearchResultItem.FormatRelativeTime(published)}·{ReleaseType(releaseType)}";

    private static string ReleaseType(FileReleaseType type) => type switch
    {
        FileReleaseType.Release => "正式版",
        FileReleaseType.Beta => "测试B版",
        FileReleaseType.Alpha => "测试A版",
        _ => "测试版"
    };

    private static bool IsMinecraftVersion(string version) => Regex.IsMatch(version,
        @"^\d+\.\d+(?:\.\d+)?(?:-(?:snapshot|pre-release|pre\d+|rc\d+))?$", RegexOptions.IgnoreCase);
}

public static class JavaResourceDownload
{
    public static async Task ShowInstallDialogAsync(TopLevel topLevel, JavaResourceDefinition definition,
        JavaResourceFileItem file)
    {
        var result = await OverlayDialog
            .ShowCustomAsync<JavaResourceInstallDialog, JavaResourceInstallDialogViewModel,
                JavaResourceInstallDialogResult>(
                new JavaResourceInstallDialogViewModel(definition, file, InstanceManager.Instance.Instances),
                topLevel.TryGetHostId(), new OverlayDialogOptions
                {
                    Title = $"下载{definition.DisplayName}", Buttons = DialogButton.None,
                    CanLightDismiss = false, CanResize = false
                });
        if (result is null) return;
        if (result.Destination == JavaResourceDownloadDestination.SaveAs)
        {
            await DownloadAsync(topLevel, definition, file);
            return;
        }
        if (result.Instance is null) return;

        string folder;
        if (definition.Kind == JavaResourceKind.DataPack)
        {
            if (result.World is null || await new WorldSaveService().IsWorldLockedAsync(result.World.FolderPath))
            {
                NotificationGateway.Notice(topLevel, "存档正在使用，无法安装数据包", NotificationType.Warning);
                return;
            }
            folder = Path.Combine(result.World.FolderPath, "datapacks");
        }
        else
        {
            var specialFolder = definition.Kind == JavaResourceKind.ResourcePack
                ? MinecraftSpecialFolder.ResourcePacksFolder
                : MinecraftSpecialFolder.ShaderPacksFolder;
            folder = result.Instance.GetSpecialFolder(specialFolder);
        }
        Install(topLevel, definition, file, folder);
    }

    public static async Task DownloadAsync(TopLevel topLevel, JavaResourceDefinition definition,
        JavaResourceFileItem file)
    {
        var selected = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"下载{definition.DisplayName}",
            SuggestedFileName = file.FileName,
            FileTypeChoices = [new FilePickerFileType(definition.DisplayName) { Patterns = Patterns(definition.Kind) }]
        });
        var destination = selected?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(destination)) return;

        StartDownload(topLevel, definition, file, destination);
    }

    public static void Install(TopLevel topLevel, JavaResourceDefinition definition, JavaResourceFileItem file,
        string folder)
    {
        Directory.CreateDirectory(folder);
        var fileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(fileName)) throw new InvalidDataException("资源文件名无效。");
        StartDownload(topLevel, definition, file, Path.Combine(folder, fileName));
    }

    private static void StartDownload(TopLevel topLevel, JavaResourceDefinition definition,
        JavaResourceFileItem file, string destination)
    {
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = $"下载{definition.DisplayName}：{file.FileName}",
            Description = "正在连接下载服务器",
            Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = "取消下载", Description = $"取消此{definition.DisplayName}下载", IconKey = "Cancel",
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
            context.SetRunning($"正在下载：{file.FileName}");
            var request = new DownloadRequest(file.DownloadUrl, destination, file.FileSize)
            {
                ProgressChanged = progress => Dispatcher.UIThread.Post(() =>
                {
                    if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                    context.ReportProgress(progress.TotalBytes > 0
                        ? Math.Clamp((double)progress.DownloadedBytes / progress.TotalBytes, 0, 1)
                        : null);
                    context.SetDescription($"下载速度：{DefaultDownloader.FormatSize(progress.Speed, true)}");
                })
            };
            var result = await new DefaultDownloader().DownloadAsync(request, context.CancellationToken);
            if (result.Type == DownloadResultType.Cancelled) throw new OperationCanceledException(context.CancellationToken);
            if (result.Type != DownloadResultType.Successful) throw result.Exception ?? new IOException("下载失败。");
            context.ReportProgress(1);
            context.SetDescription("下载完成");
        });
        task.Start();
        _ = ObserveAsync(task, topLevel, file.FileName);
    }

    private static IReadOnlyList<string> Patterns(JavaResourceKind kind) => kind switch
    {
        JavaResourceKind.ResourcePack or JavaResourceKind.ShaderPack or JavaResourceKind.DataPack => ["*.zip"],
        _ => ["*.*"]
    };

    private static async Task ObserveAsync(ManagedTask task, TopLevel topLevel, string fileName)
    {
        try { await task.Completion; } catch { }
        if (task.Status == ManagedTaskStatus.Completed)
            Dispatcher.UIThread.Post(() => NotificationGateway.Notice(topLevel, $"{fileName} 下载完成", NotificationType.Success));
        else if (task.Status == ManagedTaskStatus.Faulted)
            Dispatcher.UIThread.Post(() => NotificationGateway.Notice(topLevel, $"{fileName} 下载失败", NotificationType.Error));
        await Task.Delay(TimeSpan.FromSeconds(3));
        Dispatcher.UIThread.Post(() => TaskManager.Instance.RemoveTerminalTask(task));
    }
}

public sealed class ModpackDetailsPageViewModel(JavaResourceDetailsTarget target) : JavaResourceDetailsViewModel(target);
public sealed class ResourcePackDetailsPageViewModel(JavaResourceDetailsTarget target) : JavaResourceDetailsViewModel(target);
public sealed class ShaderPackDetailsPageViewModel(JavaResourceDetailsTarget target) : JavaResourceDetailsViewModel(target);
public sealed class DataPackDetailsPageViewModel(JavaResourceDetailsTarget target) : JavaResourceDetailsViewModel(target);

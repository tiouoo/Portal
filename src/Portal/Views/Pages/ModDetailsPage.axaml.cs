using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using AsyncImageLoader;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Provider;
using MinecraftLaunch.Components.Downloader;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Views.Pages.InstancePages;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Views.Pages;

public enum ModDetailsSource
{
    Modrinth,
    CurseForge
}

// Instance entries use only the provider identity cached for their installed file.
// All display data is fetched from the provider project endpoint.
public sealed record ModDetailsTarget(
    ModDetailsSource Source,
    string ProjectId,
    string GameVersion = "",
    ModLoaderType Loader = ModLoaderType.Any);

public partial class ModDetailsPage : UserControl, ITioTabPage
{
    public ModDetailsPage() : this(new ModDetailsTarget(ModDetailsSource.Modrinth, string.Empty))
    {
    }

    public ModDetailsPage(ModDetailsTarget target)
    {
        InitializeComponent();
        ViewModel = new ModDetailsPageViewModel(target);
        DataContext = ViewModel;
        PageInfo = new PageInfo
        {
            Title = "模组详情",
            Icon = StreamGeometry.Parse(
                "F1 M640,640z M0,0z M560.3,301.2C570.7,313 588.6,315.6 602.1,306.7 616.8,296.9 620.8,277 611,262.3L563,190.3C560.2,186.1,556.4,182.6,551.9,180.1L351.4,68.7C332.1,58,308.6,58,289.2,68.7L88.8,180C83.4,183,79.1,187.4,76.2,192.8L27.7,282.7C15.1,306.1,23.9,335.2,47.3,347.8L80.3,365.5 80.3,418.8C80.3,441.8,92.7,463.1,112.7,474.5L288.7,574.2C308.3,585.3,332.2,585.3,351.8,574.2L527.8,474.5C547.9,463.1,560.2,441.9,560.2,418.8L560.2,301.3z M320.3,291.4L170.2,208 320.3,124.6 470.4,208 320.3,291.4z M278.8,341.6L257.5,387.8 91.7,299 117.1,251.8 278.8,341.6z")
        };
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    public ModDetailsPageViewModel ViewModel { get; }
    public PageInfo PageInfo { get; init; }
    public TabEntry HostTab { get; set; }

    public void OnClose() => ViewModel.Dispose();

    private async void VersionFile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ModVersionFileItem file } ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var result = await OverlayDialog
            .ShowCustomAsync<ModInstallDialog, ModInstallDialogViewModel, ModInstallDialogResult>(
                new ModInstallDialogViewModel(file, InstanceManager.Instance.Instances), topLevel.TryGetHostId(),
                new OverlayDialogOptions
                    { Title = "下载模组", Buttons = DialogButton.None, CanLightDismiss = false, CanResize = false });
        if (result is null)
            return;

        string? destination = result.Destination == ModDownloadDestination.Install && result.Instance is not null
            ? Path.Combine(result.Instance.GetSpecialFolder(MinecraftSpecialFolder.ModsFolder), file.FileName)
            : await SelectSaveDestinationAsync(topLevel, file);
        if (string.IsNullOrWhiteSpace(destination))
            return;

        StartDownload(topLevel, file, destination);
    }

    private static async Task<string?> SelectSaveDestinationAsync(TopLevel topLevel, ModVersionFileItem file)
    {
        var selected = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "另存为模组",
            SuggestedFileName = file.FileName,
            FileTypeChoices = [new FilePickerFileType("Java 模组") { Patterns = ["*.jar"] }]
        });
        return selected?.TryGetLocalPath();
    }

    private static void StartDownload(TopLevel topLevel, ModVersionFileItem file, string destination)
    {
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = $"下载模组：{file.FileName}",
            Description = "正在连接下载服务器",
            Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = "取消下载", Description = "取消此模组下载", IconKey = "Cancel",
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
                    var fraction = progress.TotalBytes > 0
                        ? Math.Clamp((double)progress.DownloadedBytes / progress.TotalBytes, 0, 1)
                        : (double?)null;
                    context.ReportProgress(fraction);
                    context.SetDescription($"下载速度：{DefaultDownloader.FormatSize(progress.Speed, true)}");
                })
            };
            var download = await new DefaultDownloader().DownloadAsync(request, context.CancellationToken);
            if (download.Type == DownloadResultType.Cancelled)
                throw new OperationCanceledException(context.CancellationToken);
            if (download.Type != DownloadResultType.Successful)
                throw download.Exception ?? new IOException("模组下载失败。");
            context.ReportProgress(1);
            context.SetDescription("下载完成");
        });
        task.Start();
        _ = ObserveDownloadAsync(task, topLevel, file.FileName);
    }

    private static async Task ObserveDownloadAsync(ManagedTask task, TopLevel topLevel, string fileName)
    {
        try
        {
            await task.Completion;
        }
        catch
        {
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

    public static void Open(TopLevel sender, ModDetailsTarget target, string? title = null)
    {
        if (sender is not TioTabWindowBase window || string.IsNullOrWhiteSpace(target.ProjectId))
            return;
        var tab = title is null
            ? new TabEntry(window, new ModDetailsPage(target))
            : new TabEntry(window, new ModDetailsPage(target), title: title);
        window.CreateTab(tab);
        window.SelectTab(tab);
    }
}

public partial class ModDetailsPageViewModel(ModDetailsTarget target) : ObservableObject, IDisposable
{
    private readonly ModrinthProvider _modrinth = new();
    private readonly CurseforgeProvider _curseforge = new();
    private bool _loaded;
    private bool _buildingFilters;
    private bool _disposed;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private CancellationTokenSource? _filterCancellation;
    private CancellationTokenSource? _filterDebounce;
    public ObservableCollection<ModMinecraftVersionFilter> VersionFilters { get; } = [];
    public ObservableCollection<ModLoaderFilter> LoaderFilters { get; } = [];
    [ObservableProperty] public partial ObservableCollection<ModVersionGroup> VersionGroups { get; set; } = [];
    public ObservableCollection<string> Screenshots { get; } = [];
    public ObservableCollection<int> ScreenshotIndices { get; } = [];
    public IAsyncImageLoader ImageLoader { get; } = new ModImageLoader();
    public IAsyncImageLoader ScreenshotLoader { get; } = new ModScreenshotLoader();
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string FriendlyName { get; set; } = string.Empty;
    [ObservableProperty] public partial string Summary { get; set; } = string.Empty;
    [ObservableProperty] public partial string Metadata { get; set; } = string.Empty;
    [ObservableProperty] public partial string? IconUrl { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool HasError { get; set; }
    [ObservableProperty] public partial ModMinecraftVersionFilter? SelectedVersionFilter { get; set; }
    [ObservableProperty] public partial ModLoaderFilter? SelectedLoaderFilter { get; set; }
    [ObservableProperty] public partial int SelectedScreenshotIndex { get; set; }
    public string SourceName => target.Source == ModDetailsSource.Modrinth ? "Modrinth" : "CurseForge";
    public bool HasVersions => VersionFilters.Count > 0;
    public bool HasScreenshots => Screenshots.Count > 0;
    public bool IsEmpty => !IsLoading && !HasError && VersionGroups.Count == 0;
    private IReadOnlyList<ModVersionFileItem> Files { get; set; } = [];

    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        IsLoading = true;
        try
        {
            var cancellationToken = _disposeCancellation.Token;
            if (target.Source == ModDetailsSource.Modrinth)
            {
                var project = await _modrinth.SearchByProjectIdAsync(target.ProjectId, cancellationToken);
                Name = project.Name;
                FriendlyName = WikiEntries.FindChineseName(project.Slug) ?? project.Name;
                Summary = project.Summary;
                IconUrl = project.IconUrl;
                Metadata = FormatMetadata(project.Updated, project.DownloadCount, "Modrinth");
                AddScreenshots(project.Screenshots);
                Files = await Task.Run(async () => (await _modrinth.GetModFilesByProjectIdAsync(target.ProjectId,
                    cancellationToken)).Select(ModVersionFileItem.From).ToArray(), cancellationToken);
            }
            else
            {
                var project =
                    (await _curseforge.GetResourcesByModIdsAsync([long.Parse(target.ProjectId)], cancellationToken))
                    .First();
                Name = project.Name;
                FriendlyName = WikiEntries.FindChineseName(project.Slug) ?? project.Name;
                Summary = project.Summary;
                IconUrl = project.IconUrl;
                Metadata = FormatMetadata(project.DateModified, project.DownloadCount, "CurseForge");
                AddScreenshots(project.Screenshots);
                Files = await Task.Run(async () => (await _curseforge.GetModFilesAsync(project.Id, cancellationToken))
                    .Select(ModVersionFileItem.From).ToArray(), cancellationToken);
            }

            await BuildFiltersAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            HasError = true;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    partial void OnSelectedVersionFilterChanged(ModMinecraftVersionFilter? value)
    {
        if (!_buildingFilters) DebounceFilter();
    }

    partial void OnSelectedLoaderFilterChanged(ModLoaderFilter? value)
    {
        if (!_buildingFilters) DebounceFilter();
    }

    private void DebounceFilter()
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellation.Token);
        var previous = Interlocked.Exchange(ref _filterDebounce, cts);
        previous?.Cancel();
        _ = ApplyFilterDebouncedAsync(cts.Token);
    }

    private async Task ApplyFilterDebouncedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(100, cancellationToken);
            await ApplyFilterAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task BuildFiltersAsync(CancellationToken cancellationToken)
    {
        var filterData = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var families = Files.SelectMany(file => file.MinecraftVersions).Select(GetVersionFamily)
                .Where(family => family != null).Distinct()
                .OrderByDescending(family => MinecraftVersionKey.Parse(family!))
                .Select(family => family!).ToArray();
            var loaders = Files.SelectMany(file => file.GroupKeys).Select(key => key.Loader).Distinct().Order()
                .ToArray();
            return (Families: families, Loaders: loaders);
        }, cancellationToken);
        if (cancellationToken.IsCancellationRequested) return;

        _buildingFilters = true;
        VersionFilters.Clear();
        VersionFilters.Add(new ModMinecraftVersionFilter("全部", null));
        foreach (var family in filterData.Families) VersionFilters.Add(new ModMinecraftVersionFilter(family, family));
        LoaderFilters.Clear();
        LoaderFilters.Add(new ModLoaderFilter("全部", null));
        foreach (var loader in filterData.Loaders) LoaderFilters.Add(new ModLoaderFilter(loader, loader));
        SelectedVersionFilter =
            VersionFilters.FirstOrDefault(filter => filter.Family == GetVersionFamily(target.GameVersion)) ??
            VersionFilters[0];
        SelectedLoaderFilter = LoaderFilters.FirstOrDefault(filter => filter.Loader == LoaderName(target.Loader)) ??
                               LoaderFilters[0];
        _buildingFilters = false;
        OnPropertyChanged(nameof(HasVersions));
        await ApplyFilterAsync();
    }

    private async Task ApplyFilterAsync()
    {
        var selectedFamily = SelectedVersionFilter?.Family;
        var selectedLoader = SelectedLoaderFilter?.Loader;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellation.Token);
        var previous = Interlocked.Exchange(ref _filterCancellation, cancellation);
        previous?.Cancel();
        try
        {
            var groups = await Task.Run(() => Files.Where(file =>
                    selectedFamily == null ||
                    file.MinecraftVersions.Any(version => GetVersionFamily(version) == selectedFamily))
                .SelectMany(file => file.GroupKeys.Select(key => (Key: key, File: file)))
                .Where(item =>
                    (selectedFamily == null || GetVersionFamily(item.Key.MinecraftVersion) == selectedFamily) &&
                    (selectedLoader == null || item.Key.Loader == selectedLoader))
                .GroupBy(item => item.Key)
                .OrderByDescending(group => MinecraftVersionKey.Parse(group.Key.MinecraftVersion))
                .ThenBy(group => group.Key.Loader)
                .Select(group => new ModVersionGroup($"{group.Key.Loader} {group.Key.MinecraftVersion}",
                    group.Select(item => item.File.ForCompatibility(item.Key)).DistinctBy(file => file.Id).ToArray()))
                .ToArray(), cancellation.Token);
            if (cancellation.IsCancellationRequested) return;

            VersionGroups = new ObservableCollection<ModVersionGroup>(groups);
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_filterCancellation, cancellation)) _filterCancellation = null;
            cancellation.Dispose();
        }
    }

    private void AddScreenshots(IEnumerable<string>? urls)
    {
        if (urls == null) return;
        foreach (var url in urls.Where(url => !string.IsNullOrWhiteSpace(url)).Distinct())
        {
            Screenshots.Add(url);
            ScreenshotIndices.Add(ScreenshotIndices.Count);
        }

        OnPropertyChanged(nameof(HasScreenshots));
    }

    private static string FormatMetadata(DateTime updated, int downloadCount, string source) =>
        $"{source}·{FormatRelativeTime(updated)}·{downloadCount:N0} 下载";

    private static string FormatRelativeTime(DateTime timestamp)
    {
        var localTime = timestamp.Kind == DateTimeKind.Utc ? timestamp.ToLocalTime() : timestamp;
        var elapsed = DateTime.Now - localTime;
        if (elapsed < TimeSpan.FromMinutes(1)) return "刚刚";
        if (elapsed < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)elapsed.TotalMinutes)} 分钟前";
        if (elapsed < TimeSpan.FromDays(1)) return $"{Math.Max(1, (int)elapsed.TotalHours)} 小时前";
        if (elapsed < TimeSpan.FromDays(2)) return "1 天前";
        if (elapsed < TimeSpan.FromDays(7)) return $"{(int)elapsed.TotalDays} 天前";
        if (elapsed < TimeSpan.FromDays(14)) return "1 周前";
        if (elapsed < TimeSpan.FromDays(30)) return $"{Math.Max(2, (int)(elapsed.TotalDays / 7))} 周前";
        if (elapsed < TimeSpan.FromDays(365)) return $"{Math.Max(1, (int)(elapsed.TotalDays / 30))} 个月前";
        return $"{Math.Max(1, (int)(elapsed.TotalDays / 365))} 年前";
    }

    private static string? GetVersionFamily(string version)
    {
        var match = Regex.Match(version, @"^(\d+)\.(\d+)(?:\.\d+)?(?:-[^-]+)?$", RegexOptions.IgnoreCase);
        return match.Success ? $"{match.Groups[1].Value}.{match.Groups[2].Value}" : null;
    }

    private static string? LoaderName(ModLoaderType loader) => loader switch
    {
        ModLoaderType.NeoForge => "NeoForge", ModLoaderType.Forge => "Forge", ModLoaderType.Fabric => "Fabric",
        ModLoaderType.Quilt => "Quilt", _ => null
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _disposeCancellation.Cancel();
        _filterCancellation?.Cancel();
        _filterDebounce?.Cancel();
        // Fast path: replace with empty collection instead of clearing (avoids N Remove notifications)
        VersionGroups = [];
        _disposeCancellation.Dispose();
    }
}

public sealed record ModMinecraftVersionFilter(string DisplayName, string? Family);

public sealed record ModLoaderFilter(string DisplayName, string? Loader);

public sealed partial class ModVersionGroup : ObservableObject
{
    private const int PageSize = 20;
    private readonly IReadOnlyList<ModVersionFileItem> _files;
    public string Title { get; }
    public ObservableCollection<ModVersionFileItem> VisibleFiles { get; } = [];
    public string FileCountText => $"{_files.Count} 个文件";
    public bool HasMore => VisibleFiles.Count < _files.Count;
    public string LoadMoreText => $"显示更多（剩余 {_files.Count - VisibleFiles.Count} 个）";

    public ModVersionGroup(string title, IReadOnlyList<ModVersionFileItem> files)
    {
        Title = title;
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

public sealed record ModVersionGroupKey(string Loader, string MinecraftVersion);

public sealed record ModVersionFileItem(
    string Id,
    string DisplayName,
    string Details,
    string ReleaseTypeText,
    string FileName,
    string DownloadUrl,
    long FileSize,
    IReadOnlyList<string> MinecraftVersions,
    IReadOnlyList<ModVersionGroupKey> GroupKeys)
{
    public static ModVersionFileItem From(ModrinthResourceFile file) => new(file.VersionId,
        string.IsNullOrWhiteSpace(file.DisplayName) ? file.FileName : file.DisplayName,
        FormatDetails(string.Join(",", file.ModLoaders.Select(LoaderName).Distinct()), file.FileName, file.Published,
            file.ReleaseType),
        ReleaseType(file.ReleaseType), file.FileName, file.DownloadUrl, file.FileSize,
        file.MinecraftVersions.ToList(),
        file.ModLoaders.SelectMany(loader =>
            file.MinecraftVersions.Select(version => new ModVersionGroupKey(LoaderName(loader), version))).ToList());

    public static ModVersionFileItem From(CurseforgeResourceFile file)
    {
        var versions = file.GameVersions.Where(IsMinecraftVersion).ToList();
        var loaders = file.GameVersions.Select(LoaderName).OfType<string>().DefaultIfEmpty("通用");
        var enumerable = loaders as string[] ?? loaders.ToArray();
        return new(file.Id.ToString(), string.IsNullOrWhiteSpace(file.DisplayName) ? file.FileName : file.DisplayName,
            FormatDetails(string.Join(",", enumerable), file.FileName, file.Published, file.ReleaseType),
            ReleaseType(file.ReleaseType), file.FileName,
            file.DownloadUrl, file.FileLength, versions,
            enumerable.SelectMany(loader => versions.Select(version => new ModVersionGroupKey(loader, version)))
                .ToList());
    }

    public ModVersionFileItem ForCompatibility(ModVersionGroupKey compatibility) => this with
    {
        Details = $"{compatibility.Loader}·{Details[(Details.IndexOf('·') + 1)..]}",
        MinecraftVersions = [compatibility.MinecraftVersion],
        GroupKeys = [compatibility]
    };

    private static string LoaderName(ModLoaderType loader) => loader switch
    {
        ModLoaderType.NeoForge => "NeoForge", ModLoaderType.Forge => "Forge", ModLoaderType.Fabric => "Fabric",
        ModLoaderType.Quilt => "Quilt", _ => "通用"
    };

    private static string? LoaderName(string loader) => loader.Trim().ToLowerInvariant() switch
    {
        "neoforge" => "NeoForge", "forge" => "Forge", "fabric" => "Fabric", "quilt" => "Quilt", _ => null
    };

    private static bool IsMinecraftVersion(string version) => Regex.IsMatch(version,
        @"^\d+\.\d+(?:\.\d+)?(?:-(?:snapshot|pre-release|pre\d+|rc\d+))?$", RegexOptions.IgnoreCase);

    private static string FormatDetails(string loader, string fileName, DateTime published, FileReleaseType releaseType) =>
        $"{loader}·{fileName}·{FormatRelativeTime(published)}·{ReleaseType(releaseType)}";

    private static string ReleaseType(FileReleaseType type) => type switch
    {
        FileReleaseType.Release => "正式版", FileReleaseType.Beta => "测试B版", FileReleaseType.Alpha => "测试A版",
        _ => "测试版"
    };

    private static string FormatRelativeTime(DateTime timestamp)
    {
        var localTime = timestamp.Kind == DateTimeKind.Utc ? timestamp.ToLocalTime() : timestamp;
        var elapsed = DateTime.Now - localTime;
        if (elapsed < TimeSpan.FromMinutes(1)) return "刚刚";
        if (elapsed < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)elapsed.TotalMinutes)} 分钟前";
        if (elapsed < TimeSpan.FromDays(1)) return $"{Math.Max(1, (int)elapsed.TotalHours)} 小时前";
        if (elapsed < TimeSpan.FromDays(2)) return "1 天前";
        if (elapsed < TimeSpan.FromDays(7)) return $"{(int)elapsed.TotalDays} 天前";
        if (elapsed < TimeSpan.FromDays(14)) return "1 周前";
        if (elapsed < TimeSpan.FromDays(30)) return $"{Math.Max(2, (int)(elapsed.TotalDays / 7))} 周前";
        if (elapsed < TimeSpan.FromDays(365)) return $"{Math.Max(1, (int)(elapsed.TotalDays / 30))} 个月前";
        return $"{Math.Max(1, (int)(elapsed.TotalDays / 365))} 年前";
    }
}

public readonly record struct MinecraftVersionKey(int Major, int Minor, int Patch) : IComparable<MinecraftVersionKey>
{
    public static MinecraftVersionKey Parse(string version)
    {
        var match = Regex.Match(version, @"^(\d+)\.(\d+)(?:\.(\d+))?");
        return match.Success
            ? new MinecraftVersionKey(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value),
                match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0)
            : new MinecraftVersionKey(-1, -1, -1);
    }

    public int CompareTo(MinecraftVersionKey other) =>
        Major != other.Major ? Major.CompareTo(other.Major) :
        Minor != other.Minor ? Minor.CompareTo(other.Minor) :
        Patch.CompareTo(other.Patch);
}

public sealed class ModScreenshotLoader : IAsyncImageLoader
{
    private const int ScreenshotWidth = 260;
    private static readonly HttpClient Client = new();

    public async Task<Bitmap?> ProvideImageAsync(string url)
    {
        try
        {
            using var response = await Client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            return Bitmap.DecodeToWidth(stream, ScreenshotWidth);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    public void Dispose()
    {
    }
}

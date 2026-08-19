using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using AsyncImageLoader;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Provider;
using Portal.Core.App.Helpers;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Models;
using Portal.Core.Minecraft.Services;
using Portal.Module.Imaging;
using Portal.Views.Pages.InstancePages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.DownloadPages;

public sealed record ModDetailsTarget(
    ModDetailsSource Source,
    string ProjectId,
    string GameVersion = "",
    ModLoaderType Loader = ModLoaderType.Any);

public partial class ModDetailsPage : ResourceDetailsPageBase
{
    public ModDetailsPage() : this(new ModDetailsTarget(ModDetailsSource.Modrinth, string.Empty))
    {
    }

    public ModDetailsPage(ModDetailsTarget target)
    {
        InitializeComponent();
        ViewModel = new ModDetailsPageViewModel(target);
        DataContext = ViewModel;
        ViewModel.TargetVersionGroupReady += OnTargetVersionGroupReady;
        PageInfo = new PageInfo
        {
            Title = "模组详情",
            Icon = StreamGeometry.Parse(
                "F1 M640,640z M0,0z M560.3,301.2C570.7,313 588.6,315.6 602.1,306.7 616.8,296.9 620.8,277 611,262.3L563,190.3C560.2,186.1,556.4,182.6,551.9,180.1L351.4,68.7C332.1,58,308.6,58,289.2,68.7L88.8,180C83.4,183,79.1,187.4,76.2,192.8L27.7,282.7C15.1,306.1,23.9,335.2,47.3,347.8L80.3,365.5 80.3,418.8C80.3,441.8,92.7,463.1,112.7,474.5L288.7,574.2C308.3,585.3,332.2,585.3,351.8,574.2L527.8,474.5C547.9,463.1,560.2,441.9,560.2,418.8L560.2,301.3z M320.3,291.4L170.2,208 320.3,124.6 470.4,208 320.3,291.4z M278.8,341.6L257.5,387.8 91.7,299 117.1,251.8 278.8,341.6z")
        };
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    public ModDetailsPageViewModel ViewModel { get; }

    public override void OnClose()
    {
        ViewModel.TargetVersionGroupReady -= OnTargetVersionGroupReady;
        base.OnClose();
        ViewModel.Dispose();
    }

    private void OnTargetVersionGroupReady(ModVersionGroup group)
    {
        QueueScrollTo(group);
    }

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

        var destination = result.Destination == ModDownloadDestination.Install && result.Instance is not null
            ? Path.Combine(result.Instance.GetSpecialFolder(MinecraftSpecialFolder.ModsFolder), file.FileName)
            : await SelectSaveDestinationAsync(topLevel, file);
        if (string.IsNullOrWhiteSpace(destination))
            return;

        StartDownload(topLevel, file, destination);
        if (result.Destination == ModDownloadDestination.Install && result.Instance is not null)
        {
            var modsFolder = result.Instance.GetSpecialFolder(MinecraftSpecialFolder.ModsFolder);
            foreach (var dependency in await ModDependencyFilter.FilterInstalledAsync(result.Instance,
                         result.Dependencies))
                StartDownload(topLevel, dependency, Path.Combine(modsFolder, dependency.FileName));
        }
    }

    public static async Task QuickDownloadAsync(TopLevel topLevel, ModDetailsTarget target)
    {
        var loading = new QuickDownloadLoadingDialogViewModel("下载模组");
        var loadingDialog = OverlayDialog
            .ShowCustomAsync<QuickDownloadLoadingDialog, QuickDownloadLoadingDialogViewModel,
                object?>(loading, topLevel.TryGetHostId(), new OverlayDialogOptions
            {
                Title = "下载模组", Buttons = DialogButton.None, CanLightDismiss = false, CanResize = false
            });
        try
        {
            IReadOnlyList<ModVersionFileItem> files = target.Source switch
            {
                ModDetailsSource.Modrinth =>
                    (await new ModrinthProvider().GetModFilesByProjectIdAsync(target.ProjectId))
                    .Select(ModVersionFileItem.From).ToArray(),
                ModDetailsSource.CurseForge => (await new CurseforgeProvider().GetModFilesAsync(
                        long.Parse(target.ProjectId)))
                    .Select(ModVersionFileItem.From).ToArray(),
                _ => []
            };
            if (files.Count == 0) throw new InvalidDataException("未找到可下载的模组文件。");
            loading.Close();
            await loadingDialog;

            await ShowInstallDialogAsync(topLevel, files);
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[ModDownload] Quick download cancelled for {target.ProjectId}: {exception}");
            loading.Fail();
            await loadingDialog;
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            loading.Fail();
            await loadingDialog;
        }
    }

    private static async Task ShowInstallDialogAsync(TopLevel topLevel, IReadOnlyList<ModVersionFileItem> files)
    {
        var result = await OverlayDialog
            .ShowCustomAsync<ModInstallDialog, ModInstallDialogViewModel, ModInstallDialogResult>(
                new ModInstallDialogViewModel(files, InstanceManager.Instance.Instances), topLevel.TryGetHostId(),
                new OverlayDialogOptions
                    { Title = "下载模组", Buttons = DialogButton.None, CanLightDismiss = false, CanResize = false });
        if (result is null) return;
        var file = result.File;

        var destination = result.Destination == ModDownloadDestination.Install && result.Instance is not null
            ? Path.Combine(result.Instance.GetSpecialFolder(MinecraftSpecialFolder.ModsFolder), file.FileName)
            : await SelectSaveDestinationAsync(topLevel, file);
        if (string.IsNullOrWhiteSpace(destination)) return;

        StartDownload(topLevel, file, destination);
        if (result.Destination == ModDownloadDestination.Install && result.Instance is not null)
        {
            var modsFolder = result.Instance.GetSpecialFolder(MinecraftSpecialFolder.ModsFolder);
            foreach (var dependency in await ModDependencyFilter.FilterInstalledAsync(result.Instance,
                         result.Dependencies))
                StartDownload(topLevel, dependency, Path.Combine(modsFolder, dependency.FileName));
        }
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
        DownloadTasks.Download(topLevel, $"下载模组：{file.FileName}", "取消此模组下载", file.FileName,
            file.DownloadUrl, destination, file.FileSize, failureMessage: "模组下载失败。");
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
    private readonly CurseforgeProvider _curseforge = new();
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly ModrinthProvider _modrinth = new();
    private IReadOnlyList<ModVersionGroup> _allVersionGroups = [];
    private bool _buildingFilters;
    private bool _disposed;
    private CancellationTokenSource? _filterCancellation;
    private CancellationTokenSource? _filterDebounce;
    private bool _hasLocatedTargetVersionGroup;
    private bool _loaded;
    private int _nextVersionGroupIndex;
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
    public bool HasMoreVersionGroups => _nextVersionGroupIndex < _allVersionGroups.Count;
    public string LoadMoreVersionGroupsText => $"显示更多版本（剩余 {_allVersionGroups.Count - _nextVersionGroupIndex} 个）";
    private IReadOnlyList<ModVersionFileItem> Files { get; set; } = [];

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _buildingFilters = true;
        _filterCancellation = null;
        _filterDebounce = null;
        CancellationTokens.CancelInBackground(_disposeCancellation);

        VersionGroups = [];
        TargetVersionGroupReady = null;
        VersionFilters.Clear();
        LoaderFilters.Clear();
        Screenshots.Clear();
        ScreenshotIndices.Clear();
        SelectedVersionFilter = null;
        SelectedLoaderFilter = null;
        Files = [];
        _allVersionGroups = [];
    }

    public event Action<ModVersionGroup>? TargetVersionGroupReady;

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
                var translations = await ProjectTranslationService.GetTranslationsAsync(
                    ProjectTranslationSource.Modrinth,
                    [project.ProjectId], cancellationToken);
                Name = project.Name;
                FriendlyName = WikiEntries.FindChineseName(project.Slug) ?? project.Name;
                Summary = translations.GetValueOrDefault(project.ProjectId) ?? project.Summary;
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
                var projectId = project.Id.ToString();
                var translations = await ProjectTranslationService.GetTranslationsAsync(
                    ProjectTranslationSource.CurseForge,
                    [projectId], cancellationToken);
                Name = project.Name;
                FriendlyName = WikiEntries.FindChineseName(project.Slug) ?? project.Name;
                Summary = translations.GetValueOrDefault(projectId) ?? project.Summary;
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
        if (!_disposed && !_buildingFilters) DebounceFilter();
    }

    partial void OnSelectedLoaderFilterChanged(ModLoaderFilter? value)
    {
        if (!_disposed && !_buildingFilters) DebounceFilter();
    }

    private void DebounceFilter()
    {
        if (_disposed) return;
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
        if (_disposed) return;
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
                    group.Select(item => item.File.ForCompatibility(item.Key)).DistinctBy(file => file.Id).ToArray(),
                    group.Key.Loader, group.Key.MinecraftVersion))
                .ToArray(), cancellation.Token);
            if (cancellation.IsCancellationRequested || _disposed) return;

            _allVersionGroups = groups;
            _nextVersionGroupIndex = 0;
            VersionGroups = [];
            LoadMoreVersionGroups();
            if (!_hasLocatedTargetVersionGroup && !string.IsNullOrWhiteSpace(target.GameVersion) &&
                VersionGroups.FirstOrDefault(group => group.MinecraftVersion == target.GameVersion &&
                                                      (LoaderName(target.Loader) is not { } targetLoader ||
                                                       group.Loader == targetLoader)) is { } targetGroup)
            {
                _hasLocatedTargetVersionGroup = true;
                targetGroup.IsExpanded = true;
                TargetVersionGroupReady?.Invoke(targetGroup);
            }

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

    [RelayCommand]
    private void LoadMoreVersionGroups()
    {
        const int pageSize = 20;
        foreach (var group in _allVersionGroups.Skip(_nextVersionGroupIndex).Take(pageSize)) VersionGroups.Add(group);
        _nextVersionGroupIndex = VersionGroups.Count;
        OnPropertyChanged(nameof(HasMoreVersionGroups));
        OnPropertyChanged(nameof(LoadMoreVersionGroupsText));
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

    private static string FormatMetadata(DateTime updated, int downloadCount, string source)
    {
        return $"{source}·{RelativeTime.Format(updated)}·{downloadCount:N0} 下载";
    }

    private static string? GetVersionFamily(string version)
    {
        var match = Regex.Match(version, @"^(\d+)\.(\d+)(?:\.\d+)?(?:-[^-]+)?$", RegexOptions.IgnoreCase);
        return match.Success ? $"{match.Groups[1].Value}.{match.Groups[2].Value}" : null;
    }

    private static string? LoaderName(ModLoaderType loader)
    {
        return loader switch
        {
            ModLoaderType.NeoForge => "NeoForge", ModLoaderType.Forge => "Forge", ModLoaderType.Fabric => "Fabric",
            ModLoaderType.Quilt => "Quilt", _ => null
        };
    }
}

public sealed record ModMinecraftVersionFilter(string DisplayName, string? Family);

public sealed record ModLoaderFilter(string DisplayName, string? Loader);

public sealed partial class ModVersionGroup : ObservableObject
{
    private const int PageSize = 20;
    private readonly IReadOnlyList<ModVersionFileItem> _files;

    public ModVersionGroup(string title, IReadOnlyList<ModVersionFileItem> files, string loader,
        string minecraftVersion)
    {
        Title = title;
        _files = files;
        Loader = loader;
        MinecraftVersion = minecraftVersion;
        LoadMore();
    }

    public string Title { get; }
    public string Loader { get; }
    public string MinecraftVersion { get; }
    public ObservableCollection<ModVersionFileItem> VisibleFiles { get; } = [];
    public string FileCountText => $"{_files.Count} 个文件";
    public bool HasMore => VisibleFiles.Count < _files.Count;
    public string LoadMoreText => $"显示更多（剩余 {_files.Count - VisibleFiles.Count} 个）";

    [ObservableProperty] public partial bool IsExpanded { get; set; }

    [RelayCommand]
    private void LoadMore()
    {
        foreach (var file in _files.Skip(VisibleFiles.Count).Take(PageSize)) VisibleFiles.Add(file);
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(LoadMoreText));
    }
}
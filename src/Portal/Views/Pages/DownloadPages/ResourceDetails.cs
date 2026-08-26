using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text.RegularExpressions;
using AsyncImageLoader;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iridium.Enums;
using Iridium.Extensions;
using Iridium.Models.Resources;
using MinecraftLaunch.Base.Enums;
using Portal.Core.App.Helpers;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Models;
using Portal.Core.Minecraft.Services;
using Portal.Core.Module.News;
using Portal.Localization;
using Portal.Module.Imaging;
using Portal.Views.Pages.InstancePages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.DownloadPages;

public sealed record ResourceDetailsTarget(
    ResourceDefinition Definition,
    ModDetailsSource Source,
    string ProjectId,
    string GameVersion = "",
    ModLoaderType Loader = ModLoaderType.Any);

internal static class ResourceProjectFiles
{
    private static readonly LruCache<(ModDetailsSource Source, string ProjectId),
        Task<IReadOnlyList<ResourceVersionFileItem>>> Cache = new(64);
    private static readonly Lock CacheLock = new();

    public static bool TryGetCached(ResourceDetailsTarget target,
        out IReadOnlyList<ResourceVersionFileItem> files)
    {
        Task<IReadOnlyList<ResourceVersionFileItem>>? task;
        lock (CacheLock)
        {
            Cache.TryGetValue((target.Source, target.ProjectId), out task);
        }

        if (task is { IsCompletedSuccessfully: true })
        {
            files = task.Result;
            return true;
        }

        files = [];
        return false;
    }

    public static async Task<IReadOnlyList<ResourceVersionFileItem>> GetAsync(ResourceDetailsTarget target,
        CancellationToken cancellationToken = default)
    {
        var key = (target.Source, target.ProjectId);
        Task<IReadOnlyList<ResourceVersionFileItem>> task;
        lock (CacheLock)
        {
            if (!Cache.TryGetValue(key, out task!))
            {
                task = LoadAsync(target.Source, target.ProjectId);
                Cache.Set(key, task);
            }
        }

        try
        {
            return await task.WaitAsync(cancellationToken);
        }
        catch when (task.IsFaulted || task.IsCanceled)
        {
            lock (CacheLock)
            {
                if (Cache.TryGetValue(key, out var cached) && ReferenceEquals(cached, task)) Cache.Remove(key);
            }

            throw;
        }
    }

    private static async Task<IReadOnlyList<ResourceVersionFileItem>> LoadAsync(ModDetailsSource source,
        string projectId)
    {
        return source switch
        {
            ModDetailsSource.Modrinth =>
                (await IridiumResourceClients.Modrinth.GetProjectFilesAsync(projectId))
                .Select(ResourceVersionFileItem.From).ToArray(),
            ModDetailsSource.CurseForge =>
                (await IridiumResourceClients.CurseForge.GetProjectFilesAsync(projectId))
                .Select(ResourceVersionFileItem.From).ToArray(),
            _ => []
        };
    }
}

public partial class ResourceDetailsViewModel : ObservableObject, IDisposable
{
    private const int GalleryPage = 2;

    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly ResourceDetailsTarget _target;
    private readonly Dictionary<int, UserControl> _pageViews;
    private IReadOnlyList<ResourceVersionGroup> _allVersionGroups = [];
    private bool _buildingFilters;
    private bool _disposed;
    private CancellationTokenSource? _filterCancellation;
    private CancellationTokenSource? _filterDebounce;
    private bool _galleryVisible;
    private bool _hasLocatedTargetVersionGroup;
    private int _currentPageIndex;
    private bool _loaded;
    private int _nextVersionGroupIndex;

    public ResourceDetailsViewModel(ResourceDetailsTarget target)
    {
        _target = target;
        _pageViews = new Dictionary<int, UserControl>
        {
            [0] = new ResourceDescriptionView(),
            [1] = new ResourceVersionsView(),
            [2] = new ResourceGalleryView()
        };
        CurrentPage = _pageViews[0];
    }

    public ResourceDetailsTarget Target => _target;
    public ObservableCollection<ResourceVersionFilter> VersionFilters { get; } = [];
    public ObservableCollection<ResourceLoaderFilter> LoaderFilters { get; } = [];
    public ObservableCollection<ResourceScreenshot> Screenshots { get; } = [];
    public ObservableCollection<Control> DescriptionControls { get; } = [];
    public ObservableCollection<string> Tags { get; } = [];
    public ObservableCollection<string> NavigationItems { get; } =
    [
        DownloadsLanguageManager.Instance.resourcedetailspage_description.CurrentValue(),
        DownloadsLanguageManager.Instance.resourcedetailspage_versions.CurrentValue()
    ];
    public ObservableCollection<int> ScreenshotIndices { get; } = [];
    public IAsyncImageLoader ImageLoader { get; } = new ModImageLoader();
    public IAsyncImageLoader ScreenshotLoader { get; } = new ModScreenshotLoader();
    [ObservableProperty] public partial ObservableCollection<ResourceVersionGroup> VersionGroups { get; set; } = [];
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string FriendlyName { get; set; } = string.Empty;
    [ObservableProperty] public partial string Summary { get; set; } = string.Empty;
    public string OriginalSummary { get; set; } = string.Empty;
    [ObservableProperty] public partial UserControl? CurrentPage { get; set; }
    [ObservableProperty] public partial string SelectedNavigation { get; set; } =
        DownloadsLanguageManager.Instance.resourcedetailspage_description.CurrentValue();
    [ObservableProperty] public partial bool IsFavorite { get; set; }
    [ObservableProperty] public partial string Metadata { get; set; } = string.Empty;
    [ObservableProperty] public partial string? IconUrl { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; } = true;
    [ObservableProperty] public partial bool HasError { get; set; }
    [ObservableProperty] public partial ResourceVersionFilter? SelectedVersionFilter { get; set; }
    [ObservableProperty] public partial ResourceLoaderFilter? SelectedLoaderFilter { get; set; }
    [ObservableProperty] public partial int SelectedScreenshotIndex { get; set; }
    public string DisplayName => string.IsNullOrWhiteSpace(FriendlyName) ? Name : FriendlyName;
    public string Subtitle => string.IsNullOrWhiteSpace(FriendlyName) ? string.Empty : Name;
    public string SourceName => Target.Source == ModDetailsSource.Modrinth ? "Modrinth" : "CurseForge";
    public bool HasFriendlyName => !string.IsNullOrWhiteSpace(FriendlyName);
    public bool ShowLoaderFilter => Target.Definition.SupportsLoaderFilter;
    public string LoadingText => string.Format(
        CommonLanguageManager.Instance.resourceDetails_loading.CurrentValue(), Target.Definition.DisplayName);
    public string ErrorText => string.Format(
        CommonLanguageManager.Instance.resourceDetails_error.CurrentValue(), Target.Definition.DisplayName);
    public bool HasScreenshots => Screenshots.Count > 0;
    public bool HasDescription => DescriptionControls.Count > 0;
    public bool HasTags => Tags.Count > 0;
    public bool HasVersions => VersionFilters.Count > 0;
    public bool IsEmpty => !IsLoading && !HasError && VersionGroups.Count == 0;
    public bool SupportsDownload => Target.Definition.SupportsDownload;
    public bool HasMoreVersionGroups => _nextVersionGroupIndex < _allVersionGroups.Count;
    public string LoadMoreVersionGroupsText => string.Format(
        CommonLanguageManager.Instance.modDetails_loadMoreVersionGroups.CurrentValue(),
        _allVersionGroups.Count - _nextVersionGroupIndex);
    private IReadOnlyList<ResourceVersionFileItem> AllFiles { get; set; } = [];

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _buildingFilters = true;
        _filterCancellation = null;
        _filterDebounce = null;
        CancellationTokens.CancelInBackground(_disposeCancellation);
        TargetVersionGroupReady = null;
        CurrentPage = null;
        _pageViews.Clear();
        VersionGroups = [];
        VersionFilters.Clear();
        LoaderFilters.Clear();
        Screenshots.Clear();
        DescriptionControls.Clear();
        Tags.Clear();
        ScreenshotIndices.Clear();
        SelectedVersionFilter = null;
        SelectedLoaderFilter = null;
        AllFiles = [];
        _allVersionGroups = [];
    }

    public event Action<ResourceVersionGroup>? TargetVersionGroupReady;

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
                var project = await IridiumResourceClients.Modrinth.GetProjectAsync(Target.ProjectId, cancellationToken)
                              ?? throw new InvalidDataException(
                                  CommonLanguageManager.Instance.quickDownload_noFiles.CurrentValue());
                var translations = await ProjectTranslationService.GetTranslationsAsync(
                    ProjectTranslationSource.Modrinth,
                    [project.Id], cancellationToken);
                Name = project.Title ?? string.Empty;
                FriendlyName = Target.Definition.Kind == ResourceKind.Mod
                    ? WikiEntries.FindChineseName(project.Slug ?? string.Empty) ?? project.Title ?? string.Empty
                    : string.Empty;
                OriginalSummary = project.Description ?? string.Empty;
                Summary = translations.GetValueOrDefault(project.Id) ?? OriginalSummary;
                IconUrl = project.IconUrl;
                Metadata = FormatMetadata(project.DateModified ?? default, (int)project.Downloads);
                SetProjectDetails(project);
                AddScreenshots(project.Screenshots);
                AllFiles = await ResourceProjectFiles.GetAsync(Target, cancellationToken);
            }
            else
            {
                var project = await IridiumResourceClients.CurseForge.GetProjectAsync(Target.ProjectId,
                    cancellationToken) ?? throw new InvalidDataException(
                    CommonLanguageManager.Instance.quickDownload_noFiles.CurrentValue());
                var projectId = project.Id;
                var translations = await ProjectTranslationService.GetTranslationsAsync(
                    ProjectTranslationSource.CurseForge,
                    [projectId], cancellationToken);
                Name = project.Title ?? string.Empty;
                FriendlyName = Target.Definition.Kind == ResourceKind.Mod
                    ? WikiEntries.FindChineseName(project.Slug ?? string.Empty) ?? project.Title ?? string.Empty
                    : string.Empty;
                OriginalSummary = project.Description ?? string.Empty;
                Summary = translations.GetValueOrDefault(projectId) ?? OriginalSummary;
                IconUrl = project.IconUrl;
                Metadata = FormatMetadata(project.DateModified ?? default, (int)project.Downloads);
                SetProjectDetails(project);
                AddScreenshots(project.Screenshots);
                AllFiles = await ResourceProjectFiles.GetAsync(Target, cancellationToken);
            }

            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Subtitle));
            OnPropertyChanged(nameof(HasFriendlyName));
            await BuildFiltersAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
            Logger.Debug($"[Download] Details loading cancelled for {Target.ProjectId}.");
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            HasError = true;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private string FormatMetadata(DateTime updated, int downloadCount)
    {
        return string.Format(CommonLanguageManager.Instance.modDetails_metadataWithSource.CurrentValue(), SourceName,
            RelativeTime.Format(updated), downloadCount);
    }

    private void SetProjectDetails(ResourceProject project)
    {
        DescriptionControls.Clear();
        var body = project.Body;
        if (!string.IsNullOrWhiteSpace(body))
            foreach (var control in NewsHtmlRenderer.RenderContent(body)) DescriptionControls.Add(control);
        else if (!string.IsNullOrWhiteSpace(OriginalSummary))
            DescriptionControls.Add(new TextBlock { Text = OriginalSummary, TextWrapping = Avalonia.Media.TextWrapping.Wrap });

        Tags.Clear();
        foreach (var tag in project.Categories.Select(ResourceSearchPresentation.LocalizeCategory)
                     .Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct()) Tags.Add(tag);
        IsFavorite = FavoriteCollectionService.Instance.Contains(CreateFavoriteResource());
        OnPropertyChanged(nameof(HasDescription));
        OnPropertyChanged(nameof(HasTags));
    }

    private FavoriteResource CreateFavoriteResource() => new()
    {
        Name = DisplayName,
        Summary = OriginalSummary,
        IconUrl = IconUrl,
        Edition = Target.Definition.Kind is ResourceKind.BedrockBehaviorPack or ResourceKind.BedrockResourcePack
            or ResourceKind.BedrockWorld or ResourceKind.BedrockWorldTemplate ? FavoriteEdition.Bedrock : FavoriteEdition.Java,
        Kind = Target.Definition.Kind,
        Source = Target.Source,
        ProjectId = Target.ProjectId,
        Tags =
        [
            Target.Source == ModDetailsSource.CurseForge ? "CurseForge" : "Modrinth",
            .. Tags.Take(2)
        ]
    };

    [RelayCommand]
    private void ToggleFavorite()
    {
        var service = FavoriteCollectionService.Instance;
        var resource = CreateFavoriteResource();
        if (IsFavorite) service.Remove(resource);
        else service.Add(resource);
        IsFavorite = !IsFavorite;
    }

    [RelayCommand]
    private void ShowDescription() => SelectPage(0);

    [RelayCommand]
    private void ShowVersions() => SelectPage(1);

    [RelayCommand]
    private void ShowGallery() => SelectPage(2);

    private void SelectPage(int page)
    {
        if (page == GalleryPage && !_galleryVisible)
            return;

        _currentPageIndex = page;
        SelectedNavigation = NavigationItems[page];
        if (_pageViews.TryGetValue(page, out var view)) CurrentPage = view;
    }

    private void SetGalleryVisible(bool visible)
    {
        if (_galleryVisible == visible)
            return;

        _galleryVisible = visible;
        if (visible)
        {
            NavigationItems.Add(DownloadsLanguageManager.Instance.resourcedetailspage_gallery.CurrentValue());
        }
        else
        {
            NavigationItems.Remove(DownloadsLanguageManager.Instance.resourcedetailspage_gallery.CurrentValue());
            if (_currentPageIndex == GalleryPage)
                ShowDescription();
        }
    }

    partial void OnSelectedNavigationChanged(string value)
    {
        var page = NavigationItems.IndexOf(value);
        if (page >= 0 && page != _currentPageIndex) SelectPage(page);
    }

    partial void OnSelectedVersionFilterChanged(ResourceVersionFilter? value)
    {
        if (!_disposed && !_buildingFilters) DebounceFilter();
    }

    partial void OnSelectedLoaderFilterChanged(ResourceLoaderFilter? value)
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
            var families = AllFiles.SelectMany(file => file.MinecraftVersions).Select(GetVersionFamily)
                .Where(family => family is not null).Distinct()
                .OrderByDescending(family => MinecraftVersionKey.Parse(family!))
                .Select(family => family!).ToArray();
            var loaders = ShowLoaderFilter
                ? AllFiles.SelectMany(file => file.GroupKeys).Select(key => key.Loader)
                    .Where(loader => loader != LinguaSentinels.UniversalLoader).Distinct().Order().ToArray()
                : [];
            return (Families: families, Loaders: loaders);
        }, cancellationToken);
        if (cancellationToken.IsCancellationRequested) return;

        _buildingFilters = true;
        VersionFilters.Clear();
        VersionFilters.Add(new ResourceVersionFilter(
            CommonLanguageManager.Instance.mod_all.CurrentValue(), null));
        foreach (var family in filterData.Families) VersionFilters.Add(new ResourceVersionFilter(family, family));
        LoaderFilters.Clear();
        LoaderFilters.Add(new ResourceLoaderFilter(CommonLanguageManager.Instance.mod_all.CurrentValue(), null));
        foreach (var loader in filterData.Loaders) LoaderFilters.Add(new ResourceLoaderFilter(loader, loader));
        SelectedVersionFilter =
            VersionFilters.FirstOrDefault(filter => filter.Family == GetVersionFamily(Target.GameVersion)) ??
            VersionFilters[0];
        SelectedLoaderFilter = LoaderFilters.FirstOrDefault(filter => filter.Loader == LoaderName(Target.Loader)) ??
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
            var groups = await Task.Run(() =>
            {
                var pairs = AllFiles.Where(file =>
                        selectedFamily is null ||
                        file.MinecraftVersions.Any(version => GetVersionFamily(version) == selectedFamily))
                    .SelectMany(file => file.GroupKeys.Select(key => (Key: key, File: file)))
                    .Where(item =>
                        (selectedFamily is null || GetVersionFamily(item.Key.MinecraftVersion) == selectedFamily) &&
                        (selectedLoader is null || item.Key.Loader == selectedLoader))
                    .ToArray();

                if (selectedLoader is not null)
                {
                    return pairs
                        .GroupBy(item => item.Key)
                        .OrderByDescending(group => MinecraftVersionKey.Parse(group.Key.MinecraftVersion))
                        .ThenBy(group => group.Key.Loader)
                        .Select(group => new ResourceVersionGroup($"{group.Key.Loader} {group.Key.MinecraftVersion}",
                            group.Select(item => item.File.ForCompatibility(item.Key)).DistinctBy(file => file.Id)
                                .ToArray(),
                            group.Key.Loader, group.Key.MinecraftVersion))
                        .ToArray();
                }

                return pairs
                    .GroupBy(item => item.Key.MinecraftVersion)
                    .OrderByDescending(group => MinecraftVersionKey.Parse(group.Key))
                    .Select(group => new ResourceVersionGroup(group.Key,
                        group.Select(item => item.File).DistinctBy(file => file.Id)
                            .OrderByDescending(file => file.Published).ToArray(),
                        string.Empty, group.Key))
                    .ToArray();
            }, cancellation.Token);
            if (cancellation.IsCancellationRequested || _disposed) return;

            _allVersionGroups = groups;
            _nextVersionGroupIndex = 0;
            VersionGroups = [];
            LoadMoreVersionGroups();
            if (!_hasLocatedTargetVersionGroup && !string.IsNullOrWhiteSpace(Target.GameVersion) &&
                VersionGroups.FirstOrDefault(group => group.MinecraftVersion == Target.GameVersion &&
                                                      (LoaderName(Target.Loader) is not { } targetLoader ||
                                                       string.IsNullOrEmpty(group.Loader) ||
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

    private void AddScreenshots(IEnumerable<ResourceScreenshotInfo>? items)
    {
        if (items is not null)
        {
            foreach (var item in items.Where(entry => !string.IsNullOrWhiteSpace(entry.Url))
                         .DistinctBy(entry => entry.Url))
            {
                Screenshots.Add(new ResourceScreenshot(item.Url, ScreenshotName(item.Url, item.Title, Screenshots.Count), item.FullUrl));
                ScreenshotIndices.Add(ScreenshotIndices.Count);
            }
        }

        OnPropertyChanged(nameof(HasScreenshots));
        SetGalleryVisible(Screenshots.Count > 0);
    }

    private static string ScreenshotName(string url, string? title, int index)
    {
        if (!string.IsNullOrWhiteSpace(title))
            return title.Trim();

        return string.Format(DownloadsLanguageManager.Instance.resourcedetailspage_screenshotIndex.CurrentValue(),
            index + 1);
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

public sealed record ResourceVersionFilter(string DisplayName, string? Family);

public sealed record ResourceLoaderFilter(string DisplayName, string? Loader);

public sealed record ResourceScreenshot(string Url, string Name, string? FullUrl = null);

public sealed partial class ResourceVersionGroup : ObservableObject
{
    private const int PageSize = 20;
    private readonly IReadOnlyList<ResourceVersionFileItem> _files;

    public ResourceVersionGroup(string title, IReadOnlyList<ResourceVersionFileItem> files, string loader,
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
    public ObservableCollection<ResourceVersionFileItem> VisibleFiles { get; } = [];
    public string FileCountText => string.Format(CommonLanguageManager.Instance.mod_fileCount.CurrentValue(),
        _files.Count);
    public bool HasMore => VisibleFiles.Count < _files.Count;
    public string LoadMoreText => string.Format(CommonLanguageManager.Instance.mod_loadMore.CurrentValue(),
        _files.Count - VisibleFiles.Count);
    [ObservableProperty] public partial bool IsExpanded { get; set; }

    [RelayCommand]
    private void LoadMore()
    {
        foreach (var file in _files.Skip(VisibleFiles.Count).Take(PageSize)) VisibleFiles.Add(file);
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(LoadMoreText));
    }
}

public static class ResourceDownload
{
    public static async Task QuickDownloadAsync(TopLevel topLevel, ResourceDetailsTarget target,
        string? iconUrl = null, string? suggestedName = null)
    {
        switch (target.Definition.Kind)
        {
            case ResourceKind.Mod:
                await QuickDownloadModAsync(topLevel, target);
                return;
            case ResourceKind.Modpack:
                await ModpackInstallation.InstallFromSearchAsync(topLevel, target, iconUrl, suggestedName);
                return;
            case ResourceKind.BedrockBehaviorPack or ResourceKind.BedrockResourcePack
                or ResourceKind.BedrockWorld or ResourceKind.BedrockWorldTemplate:
                await BedrockResourceDownload.QuickDownloadAsync(topLevel, target);
                return;
        }

        await QuickDownloadDefaultAsync(topLevel, target);
    }

    private static async Task QuickDownloadDefaultAsync(TopLevel topLevel, ResourceDetailsTarget target)
    {
        if (ResourceProjectFiles.TryGetCached(target, out var cachedFiles))
        {
            if (cachedFiles.Count > 0)
                await ShowInstallDialogAsync(topLevel, target.Definition, cachedFiles);
            return;
        }

        var loading = new QuickDownloadLoadingDialogViewModel(
            string.Format(CommonLanguageManager.Instance.quickDownload_title.CurrentValue(),
                target.Definition.DisplayName));
        var loadingDialog = OverlayDialog
            .ShowCustomAsync<QuickDownloadLoadingDialog, QuickDownloadLoadingDialogViewModel,
                object?>(loading, topLevel.TryGetHostId(), new OverlayDialogOptions
            {
                Title = string.Format(CommonLanguageManager.Instance.quickDownload_title.CurrentValue(),
                    target.Definition.DisplayName), Buttons = DialogButton.None,
                CanLightDismiss = false, CanResize = false
            });
        try
        {
            Logger.Info(
                $"[Download] Loading quick-download files for {target.Definition.DisplayName} project {target.ProjectId} from {target.Source}.");
            var files = await ResourceProjectFiles.GetAsync(target);
            if (files.Count == 0)
                throw new InvalidDataException(
                    CommonLanguageManager.Instance.quickDownload_noFiles.CurrentValue());
            loading.Close();
            await loadingDialog;

            await ShowInstallDialogAsync(topLevel, target.Definition, files);
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[Download] Quick download selection cancelled for project {target.ProjectId}: {exception}");
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

    public static async Task QuickDownloadModAsync(TopLevel topLevel, ResourceDetailsTarget target)
    {
        if (ResourceProjectFiles.TryGetCached(target, out var cachedFiles))
        {
            if (cachedFiles.Count > 0)
                await ShowModInstallDialogAsync(topLevel, cachedFiles);
            return;
        }

        var loading = new QuickDownloadLoadingDialogViewModel(CommonLanguageManager.Instance.modDetails_downloadMod.CurrentValue());
        var loadingDialog = OverlayDialog
            .ShowCustomAsync<QuickDownloadLoadingDialog, QuickDownloadLoadingDialogViewModel,
                object?>(loading, topLevel.TryGetHostId(), new OverlayDialogOptions
            {
                Title = CommonLanguageManager.Instance.modDetails_downloadMod.CurrentValue(),
                Buttons = DialogButton.None, CanLightDismiss = false, CanResize = false
            });
        try
        {
            var files = await ResourceProjectFiles.GetAsync(target);
            if (files.Count == 0)
                throw new InvalidDataException(CommonLanguageManager.Instance.modDetails_noModFiles.CurrentValue());
            loading.Close();
            await loadingDialog;

            await ShowModInstallDialogAsync(topLevel, files);
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

    public static async Task ShowInstallDialogAsync(TopLevel topLevel, ResourceDefinition definition,
        ResourceVersionFileItem file)
    {
        var result = await OverlayDialog
            .ShowCustomAsync<ResourceInstallDialog, ResourceInstallDialogViewModel,
                ResourceInstallDialogResult>(
                new ResourceInstallDialogViewModel(definition, file, InstanceManager.Instance.Instances),
                topLevel.TryGetHostId(), new OverlayDialogOptions
                {
                    Title = string.Format(CommonLanguageManager.Instance.quickDownload_title.CurrentValue(),
                        definition.DisplayName), Buttons = DialogButton.None,
                    CanLightDismiss = false, CanResize = false
                });
        if (result?.File is not { } selectedFile) return;
        if (result.Destination == ResourceDownloadDestination.SaveAs)
        {
            await DownloadAsync(topLevel, definition, selectedFile);
            return;
        }

        if (result.Instance is null) return;

        await InstallFromDialogAsync(topLevel, definition, selectedFile, result.Instance, result.World);
    }

    private static async Task ShowInstallDialogAsync(TopLevel topLevel, ResourceDefinition definition,
        IReadOnlyList<ResourceVersionFileItem> files)
    {
        var result = await OverlayDialog
            .ShowCustomAsync<ResourceInstallDialog, ResourceInstallDialogViewModel,
                ResourceInstallDialogResult>(
                new ResourceInstallDialogViewModel(definition, files,
                    InstanceManager.Instance.Instances),
                topLevel.TryGetHostId(), new OverlayDialogOptions
                {
                    Title = string.Format(CommonLanguageManager.Instance.quickDownload_title.CurrentValue(),
                        definition.DisplayName), Buttons = DialogButton.None,
                    CanLightDismiss = false, CanResize = false
                });
        if (result?.File is not { } file) return;
        if (result.Destination == ResourceDownloadDestination.SaveAs)
        {
            await DownloadAsync(topLevel, definition, file);
            return;
        }

        if (result.Instance is null) return;

        await InstallFromDialogAsync(topLevel, definition, file, result.Instance, result.World);
    }

    public static async Task ShowModInstallDialogAsync(TopLevel topLevel, IReadOnlyList<ResourceVersionFileItem> files)
    {
        var result = await OverlayDialog
            .ShowCustomAsync<ModInstallDialog, ModInstallDialogViewModel, ModInstallDialogResult>(
                new ModInstallDialogViewModel(files, InstanceManager.Instance.Instances), topLevel.TryGetHostId(),
                new OverlayDialogOptions
                    { Title = CommonLanguageManager.Instance.modDetails_downloadMod.CurrentValue(),
                        Buttons = DialogButton.None, CanLightDismiss = false, CanResize = false });
        if (result is null) return;
        var file = result.File;

        var destination = result.Destination == ModDownloadDestination.Install && result.Instance is not null
            ? Path.Combine(result.Instance.GetSpecialFolder(MinecraftSpecialFolder.ModsFolder), file.FileName)
            : await SelectModSaveDestinationAsync(topLevel, file);
        if (string.IsNullOrWhiteSpace(destination)) return;

        StartModDownload(topLevel, file, destination);
        if (result.Destination == ModDownloadDestination.Install && result.Instance is not null)
        {
            var modsFolder = result.Instance.GetSpecialFolder(MinecraftSpecialFolder.ModsFolder);
            foreach (var dependency in await ModDependencyFilter.FilterInstalledAsync(result.Instance,
                         result.Dependencies))
                StartModDownload(topLevel, dependency, Path.Combine(modsFolder, dependency.FileName));
        }
    }

    private static async Task<string?> SelectModSaveDestinationAsync(TopLevel topLevel, ResourceVersionFileItem file)
    {
        var selected = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = CommonLanguageManager.Instance.modDetails_saveModAs.CurrentValue(),
            SuggestedFileName = file.FileName,
            FileTypeChoices =
                [new FilePickerFileType(CommonLanguageManager.Instance.modDetails_javaMod.CurrentValue())
                {
                    Patterns = ["*.jar"]
                }]
        });
        return selected?.TryGetLocalPath();
    }

    private static void StartModDownload(TopLevel topLevel, ResourceVersionFileItem file, string destination)
    {
        DownloadTasks.Download(topLevel,
            string.Format(CommonLanguageManager.Instance.modDetails_downloadModFormat.CurrentValue(), file.FileName),
            CommonLanguageManager.Instance.modDetails_cancelModDownload.CurrentValue(), file.FileName,
            file.DownloadUrl, destination, file.FileSize,
            failureMessage: CommonLanguageManager.Instance.modDetails_modDownloadFailed.CurrentValue());
    }

    private static async Task InstallFromDialogAsync(TopLevel topLevel, ResourceDefinition definition,
        ResourceVersionFileItem file, MinecraftInstance instance, WorldSaveInfo? world)
    {
        if (definition.Kind == ResourceKind.Save)
        {
            InstallSave(topLevel, definition, file, instance.GetSpecialFolder(MinecraftSpecialFolder.SavesFolder));
            return;
        }

        string folder;
        if (definition.Kind == ResourceKind.DataPack)
        {
            if (world is null || await new WorldSaveService().IsWorldLockedAsync(world.FolderPath))
            {
                topLevel.Notice(CommonLanguageManager.Instance.javaResourceInstall_saveInUse.CurrentValue(),
                    NotificationType.Warning);
                return;
            }

            folder = Path.Combine(world.FolderPath, "datapacks");
        }
        else
        {
            var specialFolder = definition.Kind == ResourceKind.ResourcePack
                ? MinecraftSpecialFolder.ResourcePacksFolder
                : MinecraftSpecialFolder.ShaderPacksFolder;
            folder = instance.GetSpecialFolder(specialFolder);
        }

        Install(topLevel, definition, file, folder);
    }

    public static async Task DownloadAsync(TopLevel topLevel, ResourceDefinition definition,
        ResourceVersionFileItem file)
    {
        var selected = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = string.Format(CommonLanguageManager.Instance.quickDownload_title.CurrentValue(),
                definition.DisplayName),
            SuggestedFileName = file.FileName,
            FileTypeChoices = [new FilePickerFileType(definition.DisplayName) { Patterns = Patterns(definition.Kind) }]
        });
        var destination = selected?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(destination)) return;

        Logger.Info($"[Download] Exporting {definition.DisplayName} {file.FileName} to {destination}.");
        StartDownload(topLevel, definition, file, destination);
    }

    public static void Install(TopLevel topLevel, ResourceDefinition definition, ResourceVersionFileItem file,
        string folder)
    {
        Directory.CreateDirectory(folder);
        var fileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidDataException(
                CommonLanguageManager.Instance.javaResourceDownload_invalidResourceFileName.CurrentValue());
        StartDownload(topLevel, definition, file, Path.Combine(folder, fileName));
    }

    private static void InstallSave(TopLevel topLevel, ResourceDefinition definition, ResourceVersionFileItem file,
        string savesFolder)
    {
        Directory.CreateDirectory(savesFolder);
        var fileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidDataException(
                CommonLanguageManager.Instance.javaResourceDownload_invalidSaveFileName.CurrentValue());
        var temporaryPath = Path.Combine(savesFolder, $".{Guid.NewGuid():N}.zip");
        StartDownload(topLevel, definition, file, temporaryPath, true);
    }

    internal static ManagedTask StartDownload(TopLevel topLevel, ResourceDefinition definition,
        ResourceVersionFileItem file, string destination, bool extractSave = false)
    {
        Func<TaskExecutionContext, Task>? afterDownload = null;
        if (extractSave)
            afterDownload = async context =>
            {
                context.SetDescription(
                    CommonLanguageManager.Instance.javaResourceDownload_extractingSave.CurrentValue());
                await ExtractSaveAsync(destination, file.FileName, context.CancellationToken);
            };
        Logger.Info(
            $"[Download] Starting {definition.DisplayName} download {file.FileName} from {file.DownloadUrl} to {destination}; extractSave={extractSave}.");
        return DownloadTasks.Download(topLevel,
            string.Format(CommonLanguageManager.Instance.javaResourceDownload_taskName.CurrentValue(),
                definition.DisplayName, file.FileName),
            string.Format(CommonLanguageManager.Instance.javaResourceDownload_cancelDownload.CurrentValue(),
                definition.DisplayName), file.FileName, file.DownloadUrl, destination, file.FileSize,
            afterDownload, extractSave ? CommonLanguageManager.Instance.javaResourceDownload_saveInstalled.CurrentValue()
            : CommonLanguageManager.Instance.download_complete.CurrentValue());
    }

    private static IReadOnlyList<string> Patterns(ResourceKind kind)
    {
        return kind switch
        {
            ResourceKind.ResourcePack or ResourceKind.ShaderPack or ResourceKind.DataPack
                or ResourceKind.Save => ["*.zip"],
            _ => ["*.*"]
        };
    }

    private static Task ExtractSaveAsync(string archivePath, string fileName, CancellationToken cancellationToken)
    {
        return Task.Run(() => ExtractSave(archivePath, fileName, cancellationToken), cancellationToken);
    }

    private static void ExtractSave(string archivePath, string fileName, CancellationToken cancellationToken)
    {
        var savesFolder = Path.GetDirectoryName(archivePath) ?? throw new InvalidDataException(
            CommonLanguageManager.Instance.javaResourceDownload_invalidSaveDirectory.CurrentValue());
        var stagingFolder = Path.Combine(savesFolder, $".portal-{Guid.NewGuid():N}");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(stagingFolder);
            using var archive = ZipFile.OpenRead(archivePath);
            var stagingRoot = Path.GetFullPath(stagingFolder) + Path.DirectorySeparatorChar;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entryPath = Path.GetFullPath(Path.Combine(stagingFolder, entry.FullName));
                if (!entryPath.StartsWith(stagingRoot, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        CommonLanguageManager.Instance.javaResourceDownload_invalidSaveArchivePath.CurrentValue());
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(entryPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(entryPath)!);
                using var source = entry.Open();
                using var target = File.Create(entryPath);
                source.CopyToAsync(target, cancellationToken).GetAwaiter().GetResult();
            }

            cancellationToken.ThrowIfCancellationRequested();
            var worldFolder = File.Exists(Path.Combine(stagingFolder, "level.dat"))
                ? stagingFolder
                : Directory.EnumerateFiles(stagingFolder, "level.dat", SearchOption.AllDirectories)
                    .Select(Path.GetDirectoryName)
                    .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
            if (worldFolder is null)
                throw new InvalidDataException(
                    CommonLanguageManager.Instance.javaResourceDownload_invalidSaveArchive.CurrentValue());

            var baseName = Path.GetFileNameWithoutExtension(Path.GetFileName(fileName));
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "World";
            var destination = Path.Combine(savesFolder, baseName);
            for (var suffix = 2; Directory.Exists(destination); suffix++)
                destination = Path.Combine(savesFolder, $"{baseName} ({suffix})");
            Directory.Move(worldFolder, destination);
        }
        finally
        {
            if (Directory.Exists(stagingFolder)) Directory.Delete(stagingFolder, true);
            if (File.Exists(archivePath)) File.Delete(archivePath);
        }
    }
}

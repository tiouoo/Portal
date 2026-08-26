using System.Collections.ObjectModel;
using AsyncImageLoader;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iridium.Enums;
using Iridium.Models.Resources;
using MinecraftLaunch.Base.Enums;
using Portal.Core.App.Helpers;
using Portal.Core.Classes;
using Portal.Core.Const;
using Portal.Core.Minecraft.Models;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Portal.Module.Imaging;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Views.Pages.DownloadPages;

public sealed record ResourceDefinition(
    ResourceKind Kind,
    string DisplayName,
    string ProjectType,
    int? CurseForgeClassId,
    bool SupportsDownload,
    bool SupportsLoaderFilter,
    bool SupportsModrinth = true,
    int CurseForgeGameId = 432,
    bool ShowIconPlaceholder = false);

public static class ResourceDefinitions
{
    public static ResourceDefinition Mod { get; } =
        new(ResourceKind.Mod, CommonLanguageManager.Instance.resourceList_mods.CurrentValue(), "mod", null, true, true);

    public static ResourceDefinition Modpack { get; } =
        new(ResourceKind.Modpack, CommonLanguageManager.Instance.javaResourceSearch_modpack.CurrentValue(),
            "modpack", 4471, false, true, ShowIconPlaceholder: true);

    public static ResourceDefinition ResourcePack { get; } =
        new(ResourceKind.ResourcePack, CommonLanguageManager.Instance.javaResourceSearch_resourcePack.CurrentValue(),
            "resourcepack", 12, true, false);

    public static ResourceDefinition ShaderPack { get; } =
        new(ResourceKind.ShaderPack, CommonLanguageManager.Instance.javaResourceSearch_shaderPack.CurrentValue(),
            "shader", 6552, true, false);

    public static ResourceDefinition DataPack { get; } =
        new(ResourceKind.DataPack, CommonLanguageManager.Instance.javaResourceSearch_dataPack.CurrentValue(),
            "datapack", 6945, true, false);

    public static ResourceDefinition Save { get; } =
        new(ResourceKind.Save, CommonLanguageManager.Instance.javaResourceSearch_save.CurrentValue(), "world", 17,
            true, false, false);
}

public abstract partial class JavaResourceSearchViewModel : ObservableObject, IDisposable, ISearchPageViewModel
{
    private const int PageSize = 40;
    private static readonly ApplicationCache<JavaResourceSearchRequest, JavaResourceSearchPage> Cache = new();
    private readonly CancellationTokenSource _disposeCancellation = new();
    private bool _disposed;
    private CancellationTokenSource? _filterDebounce;
    private CancellationTokenSource? _gameVersionDebounce;
    private bool _initialized;
    private bool _suppressFilterSearch;

    protected JavaResourceSearchViewModel(ResourceDefinition definition)
    {
        Definition = definition;
        FilterOptions = ResourceFilterCatalog.Get(definition.Kind)
            .Select(category => new ResourceFilterOption(category.Name, category.Id, OnFilterChanged)).ToArray();
        Sources = definition.SupportsModrinth
            ?
            [
                new JavaResourceSearchSource(CommonLanguageManager.Instance.mod_all.CurrentValue(), SearchSource.All),
                new JavaResourceSearchSource("CurseForge", SearchSource.CurseForge),
                new JavaResourceSearchSource("Modrinth", SearchSource.Modrinth)
            ]
            : [new JavaResourceSearchSource("CurseForge", SearchSource.CurseForge)];
        SelectedSource = Sources.FirstOrDefault(source =>
                               source.Kind == DownloadSearchPersistence.ToUiSource(Data.ConfigEntry.DefaultDownloadSearchSource))
                           ?? Sources[0];
        SelectedLoader = Loaders.FirstOrDefault(loader => loader.Kind == Data.ConfigEntry.DownloadSearchLoader)
                         ?? Loaders[0];
        SelectedSort = SortOptions.FirstOrDefault(sort =>
                           sort.Kind == DownloadSearchPersistence.ToUiSort(Data.ConfigEntry.DefaultDownloadSearchSort))
                        ?? SortOptions[0];
        SelectedEnvironment = Environments[0];
    }

    public ResourceDefinition Definition { get; }
    public string PageTitle => string.Format(CommonLanguageManager.Instance.startPage_searchModeTitle.CurrentValue(),
        Definition.DisplayName);
    public string SearchPlaceholder => string.Format(
        CommonLanguageManager.Instance.startPage_searchPlaceholderMode.CurrentValue(), Definition.DisplayName);
    public bool ShowLoaderFilter => Definition.SupportsLoaderFilter;
    public bool ShowEnvironmentFilter => Definition.Kind == ResourceKind.DataPack;
    public bool HasFilters => FilterOptions.Count > 0;
    public bool CanUseAdvancedFilters => Definition.SupportsModrinth && SelectedSource?.Kind != SearchSource.CurseForge;
    public IReadOnlyList<ResourceFilterOption> FilterOptions { get; }
    public string FilterText => $"{CommonLanguageManager.Instance.mod_filterButton.CurrentValue()}" +
                                (ActiveFilterCount > 0 ? $" ({ActiveFilterCount})" : string.Empty);
    public int ActiveFilterCount => FilterOptions.Count(option => option.IsSelected != false) +
                                    (SelectedEnvironment?.Kind == ResourceEnvironment.Any ? 0 : 1);
    public string CategoryTitle => CommonLanguageManager.Instance.mod_filterCategories.CurrentValue();
    public string ExcludeText => CommonLanguageManager.Instance.mod_filterExclude.CurrentValue();
    public string ClearText => CommonLanguageManager.Instance.mod_filterClear.CurrentValue();
    public string InvertText => CommonLanguageManager.Instance.mod_filterInvert.CurrentValue();
    public string ModrinthOnlyText => CommonLanguageManager.Instance.mod_filterModrinthOnly.CurrentValue();
    public ObservableCollection<JavaResourceSearchResultItem> Results { get; } = [];
    public ObservableCollection<string> MinecraftVersions { get; } = [];
    public IReadOnlyList<JavaResourceSearchSource> Sources { get; }

    public IReadOnlyList<ModSearchLoader> Loaders { get; } =
    [
        new(CommonLanguageManager.Instance.mod_allLoaders.CurrentValue(), ModLoaderType.Any),
        new("Forge", ModLoaderType.Forge),
        new("NeoForge", ModLoaderType.NeoForge), new("Fabric", ModLoaderType.Fabric),
        new("Quilt", ModLoaderType.Quilt)
    ];

    public IReadOnlyList<ModSearchSort> SortOptions { get; } =
    [
        new(CommonLanguageManager.Instance.mod_sortRelevance.CurrentValue(), SearchSort.Relevance),
        new(CommonLanguageManager.Instance.mod_sortPopularity.CurrentValue(), SearchSort.Popularity),
        new(CommonLanguageManager.Instance.mod_sortUpdated.CurrentValue(), SearchSort.Updated),
        new(CommonLanguageManager.Instance.mod_sortNewest.CurrentValue(), SearchSort.Newest)
    ];

    public IReadOnlyList<ModSearchEnvironment> Environments { get; } =
    [
        new(CommonLanguageManager.Instance.mod_environmentAny.CurrentValue(), ResourceEnvironment.Any),
        new(CommonLanguageManager.Instance.mod_environmentClient.CurrentValue(), ResourceEnvironment.Client),
        new(CommonLanguageManager.Instance.mod_environmentServer.CurrentValue(), ResourceEnvironment.Server),
        new(CommonLanguageManager.Instance.mod_environmentBoth.CurrentValue(), ResourceEnvironment.ClientAndServer)
    ];

    [ObservableProperty] public partial JavaResourceSearchSource? SelectedSource { get; set; }
    [ObservableProperty] public partial ModSearchLoader? SelectedLoader { get; set; }
    [ObservableProperty] public partial ModSearchSort? SelectedSort { get; set; }
    [ObservableProperty] public partial ModSearchEnvironment? SelectedEnvironment { get; set; }
    [ObservableProperty] public partial string GameVersion { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusText { get; set; } =
        CommonLanguageManager.Instance.modSearch_preparingSearch.CurrentValue();
    [ObservableProperty] public partial bool HasError { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; } = true;
    [ObservableProperty] public partial int CurrentPage { get; set; } = 1;
    [ObservableProperty] public partial int TotalCount { get; set; }
    public bool HasResults => Results.Count > 0;
    public bool HasPages => TotalCount > 0;
    public bool IsLoadingPlaceholder => IsLoading && Results.Count == 0;
    public bool IsEmpty => !IsLoading && !HasError && Results.Count == 0;
    public string LoadingText => CommonLanguageManager.Instance.modSearch_loading.CurrentValue();
    public string EmptyText =>
        string.Format(CommonLanguageManager.Instance.javaResourceSearch_noResults.CurrentValue(),
            Definition.DisplayName);

    partial void OnIsLoadingChanged(bool value)
    {
        NotifyResultState();
    }

    partial void OnHasErrorChanged(bool value)
    {
        NotifyResultState();
    }

    partial void OnTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasPages));
    }

    private void NotifyResultState()
    {
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(IsLoadingPlaceholder));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _suppressFilterSearch = true;
        _gameVersionDebounce?.Cancel();
        _filterDebounce?.Cancel();
        _disposeCancellation.Cancel();
        Results.Clear();
        MinecraftVersions.Clear();
    }

    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;

    public void ExecuteSearch()
    {
        SearchCommand.Execute(null);
    }

    public void RefreshContent()
    {
        Results.Clear();
        HasError = false;
        IsLoading = true;
        NotifyResultState();
        if (!_initialized) return;
        if (CurrentPage != 1)
        {
            CurrentPage = 1;
            return;
        }

        _ = SearchAsync(string.IsNullOrWhiteSpace(SearchText));
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        _ = LoadVersionsAsync();
        _ = SearchAsync(true);
        await Task.CompletedTask;
    }

    partial void OnSelectedSourceChanged(JavaResourceSearchSource? value)
    {
        if (_initialized && value is not null)
            Data.ConfigEntry.DefaultDownloadSearchSource = DownloadSearchPersistence.ToCoreSource(value.Kind);

        OnPropertyChanged(nameof(CanUseAdvancedFilters));

        _suppressFilterSearch = true;
        try
        {
            if (value?.Kind == SearchSource.CurseForge)
            {
                foreach (var option in FilterOptions.Where(option => option.IsSelected is null))
                    option.IsSelected = false;
                SelectedEnvironment = Environments[0];
            }
            SelectedSort = SortOptions[0];
            GameVersion = string.Empty;
            SelectedLoader = Loaders[0];
        }
        finally
        {
            _suppressFilterSearch = false;
        }

        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(FilterText));

        RestartSearch();
    }

    private void OnFilterChanged()
    {
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(FilterText));
        if (_initialized && !_suppressFilterSearch) ScheduleFilterSearch();
    }

    partial void OnSelectedEnvironmentChanged(ModSearchEnvironment? value)
    {
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(FilterText));
        if (_initialized && !_suppressFilterSearch) RestartSearch();
    }

    [RelayCommand]
    private void ClearCategories()
    {
        _suppressFilterSearch = true;
        try
        {
            foreach (var option in FilterOptions) option.IsSelected = false;
            SelectedEnvironment = Environments[0];
        }
        finally
        {
            _suppressFilterSearch = false;
        }
        RestartSearch();
    }

    [RelayCommand]
    private void InvertCategories()
    {
        _suppressFilterSearch = true;
        try
        {
            foreach (var option in FilterOptions)
                option.IsSelected = option.IsSelected switch { true => null, null => true, false => false };
        }
        finally
        {
            _suppressFilterSearch = false;
        }
        RestartSearch();
    }

    partial void OnSelectedSortChanged(ModSearchSort? value)
    {
        if (_initialized && value is not null)
            Data.ConfigEntry.DefaultDownloadSearchSort = DownloadSearchPersistence.ToCoreSort(value.Kind);

        if (!_initialized || _suppressFilterSearch)
            return;

        RestartSearch();
    }

    partial void OnGameVersionChanged(string value)
    {
        if (!_initialized || _suppressFilterSearch)
            return;

        ScheduleGameVersionSearch();
    }

    partial void OnSelectedLoaderChanged(ModSearchLoader? value)
    {
        if (_initialized)
            Data.ConfigEntry.DownloadSearchLoader = value?.Kind ?? ModLoaderType.Any;

        if (!_initialized || _suppressFilterSearch)
            return;

        if (ShowLoaderFilter) RestartSearch();
    }

    partial void OnCurrentPageChanged(int value)
    {
        if (_initialized && value > 0) _ = SearchAsync(false);
    }

    private void ScheduleGameVersionSearch()
    {
        if (_disposed) return;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellation.Token);
        var previous = Interlocked.Exchange(ref _gameVersionDebounce, cts);
        previous?.Cancel();
        _ = RunGameVersionSearchAsync(cts.Token);
    }

    private void ScheduleFilterSearch()
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellation.Token);
        var previous = Interlocked.Exchange(ref _filterDebounce, cts);
        previous?.Cancel();
        _ = RunFilterSearchAsync(cts.Token);
    }

    private async Task RunFilterSearchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(250, cancellationToken);
            if (CurrentPage != 1) CurrentPage = 1;
            else await SearchAsync(string.IsNullOrWhiteSpace(SearchText));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunGameVersionSearchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(500, cancellationToken);
            if (_disposed || cancellationToken.IsCancellationRequested || _suppressFilterSearch) return;
            if (CurrentPage != 1)
            {
                CurrentPage = 1;
                return;
            }

            await SearchAsync(string.IsNullOrWhiteSpace(SearchText));
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private Task SearchAsync()
    {
        if (CurrentPage != 1)
        {
            CurrentPage = 1;
            return Task.CompletedTask;
        }

        return SearchAsync(string.IsNullOrWhiteSpace(SearchText));
    }

    [RelayCommand]
    private Task RetryAsync()
    {
        return SearchAsync(string.IsNullOrWhiteSpace(SearchText));
    }

    [RelayCommand]
    private Task GoToPageAsync(int page)
    {
        if (page != CurrentPage) CurrentPage = page;
        return Task.CompletedTask;
    }

    private void RestartSearch()
    {
        if (!_initialized) return;
        if (CurrentPage != 1)
        {
            CurrentPage = 1;
            return;
        }

        _ = SearchAsync(string.IsNullOrWhiteSpace(SearchText));
    }

    private async Task SearchAsync(bool isDefaultSearch = false)
    {
        if (SelectedSource is null || SelectedSort is null) return;
        var includedCategories = string.Join('|', FilterOptions.Where(option => option.IsSelected == true)
            .Select(option => option.Id).Order());
        var excludedCategories = string.Join('|', FilterOptions.Where(option => option.IsSelected is null)
            .Select(option => option.Id).Order());
        var request = new JavaResourceSearchRequest(Definition.Kind, SelectedSource.Kind, SearchText.Trim(),
            GameVersion.Trim(), ShowLoaderFilter ? SelectedLoader?.Kind ?? ModLoaderType.Any : ModLoaderType.Any,
            includedCategories, excludedCategories, SelectedEnvironment?.Kind ?? ResourceEnvironment.Any,
            SelectedSort.Kind, CurrentPage);
        if (Cache.TryGetValue(request, out var cached) && cached is not null && IsCurrent(request))
        {
            Apply(cached);
            return;
        }

        if (IsCurrent(request))
        {
            Results.Clear();
            HasError = false;
            IsLoading = true;
            StatusText = isDefaultSearch
                ? string.Format(CommonLanguageManager.Instance.javaResourceSearch_fetchingPopular.CurrentValue(),
                    Definition.DisplayName)
                : CommonLanguageManager.Instance.modSearch_searching.CurrentValue();
        }

        try
        {
            var page = await Cache.GetOrCreateAsync(request, () => FetchAsync(request, CancellationToken.None));
            if (IsCurrent(request)) Apply(page);
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            if (!IsCurrent(request)) return;
            IsLoading = false;
            HasError = true;
            StatusText = CommonLanguageManager.Instance.modSearch_networkError.CurrentValue();
        }
    }

    private async Task<JavaResourceSearchPage> FetchAsync(JavaResourceSearchRequest request,
        CancellationToken cancellationToken)
    {
        var type = IridiumResourceMapping.ParseResourceType(Definition.ProjectType);
        var includedTags = ResourceFilterCatalog.Parse(request.IncludedCategories, type);
        var excludedTags = ResourceFilterCatalog.Parse(request.ExcludedCategories, type);
        var source = ResourceFilterCatalog.ResolveSource(request.Source, includedTags, excludedTags, request.Environment);
        var options = new ResourceSearchOptions
        {
            Source = source,
            CurseForgeGameId = Definition.CurseForgeGameId,
            Type = type,
            Query = string.IsNullOrWhiteSpace(request.Query) ? null : request.Query,
            GameVersion = string.IsNullOrWhiteSpace(request.GameVersion) ? null : request.GameVersion,
            Loader = request.Loader.ToResourceLoader(),
            Tags = includedTags,
            ExcludedTags = excludedTags,
            Environment = request.Environment,
            Sort = MinecraftVersionParsing.ToResourceSort(request.Sort),
            Page = request.Page,
            PageSize = PageSize
        };
        var page = await IridiumResourceClients.Search.SearchAsync(options, cancellationToken);
        var translated = await IridiumResourceClients.TranslateAsync(page.Items, cancellationToken);
        return new JavaResourceSearchPage(translated.Select(hit =>
            new JavaResourceSearchResultItem(hit, Definition, request.GameVersion, request.Loader)).ToArray(),
            page.TotalCount);
    }

    private void Apply(JavaResourceSearchPage page, bool preserveExistingItems = false)
    {
        if (preserveExistingItems)
        {
            var sharedCount = Math.Min(Results.Count, page.Items.Count);
            for (var index = 0; index < sharedCount; index++) Results[index].Update(page.Items[index]);
            while (Results.Count > page.Items.Count) Results.RemoveAt(Results.Count - 1);
            for (var index = sharedCount; index < page.Items.Count; index++) Results.Add(page.Items[index]);
        }
        else
        {
            Results.Clear();
            foreach (var item in page.Items) Results.Add(item);
        }

        TotalCount = page.TotalCount;
        HasError = false;
        IsLoading = false;
        StatusText = page.TotalCount == 0
            ? string.Format(CommonLanguageManager.Instance.javaResourceSearch_noResults.CurrentValue(),
                Definition.DisplayName)
            : string.Format(CommonLanguageManager.Instance.javaResourceSearch_resultCount.CurrentValue(),
                page.TotalCount, Definition.DisplayName);
        NotifyResultState();
    }

    private bool IsCurrent(JavaResourceSearchRequest request)
    {
        return !_disposed && Definition.Kind == request.Kind &&
               SelectedSource?.Kind == request.Source && SearchText.Trim() == request.Query &&
                GameVersion.Trim() == request.GameVersion &&
                (ShowLoaderFilter ? SelectedLoader?.Kind ?? ModLoaderType.Any : ModLoaderType.Any) == request.Loader &&
                string.Join('|', FilterOptions.Where(option => option.IsSelected == true).Select(option => option.Id).Order()) == request.IncludedCategories &&
                string.Join('|', FilterOptions.Where(option => option.IsSelected is null).Select(option => option.Id).Order()) == request.ExcludedCategories &&
                (SelectedEnvironment?.Kind ?? ResourceEnvironment.Any) == request.Environment &&
                SelectedSort?.Kind == request.Sort && CurrentPage == request.Page;
    }

    private async Task LoadVersionsAsync()
    {
        try
        {
            var versions = await MinecraftVersionLoader.LoadReleaseVersionsAsync(_disposeCancellation.Token);
            if (_disposed) return;
            MinecraftVersions.Clear();
            foreach (var version in versions) MinecraftVersions.Add(version);
        }
        catch (OperationCanceledException exception) when (_disposeCancellation.IsCancellationRequested)
        {
            Logger.Debug($"[Download] {Definition.DisplayName} version loading cancelled: {exception}");
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
        }
    }
}

public sealed partial class JavaResourceSearchResultItem : ObservableObject
{
    public JavaResourceSearchResultItem(ResourceHit hit, ResourceDefinition definition,
        string gameVersion, ModLoaderType loader)
    {
        Name = hit.Title ?? string.Empty;
        var summary = hit.Translation ?? hit.Summary ?? string.Empty;
        Summary = summary;
        Tags = ResourceSearchPresentation.BuildTags(hit);
        IconUrl = hit.IconUrl;
        Metadata = ResourceSearchPresentation.FormatMetadata(
            hit.DateModified ?? hit.DateCreated ?? default, hit.Downloads);
        Target = new ResourceDetailsTarget(definition,
            ModSearchResultItem.ToModDetailsSource(hit.Source), hit.Id, gameVersion, loader);
        IsFavorite =
            FavoriteCollectionService.Instance.Contains(FavoriteResourceFactory.From(this, GetEdition(definition)));
    }

    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial string Summary { get; set; }
    [ObservableProperty] public partial string? IconUrl { get; set; }
    [ObservableProperty] public partial string Metadata { get; set; }
    public IReadOnlyList<string> Tags { get; private set; }
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconUrl);
    [ObservableProperty] public partial bool IsFavorite { get; set; }
    public IAsyncImageLoader ImageLoader { get; } = new ModImageLoader();
    public ResourceDetailsTarget Target { get; private set; }
    public bool ShowIconPlaceholder => Target.Definition.ShowIconPlaceholder && !HasIcon;

    public void Update(JavaResourceSearchResultItem item)
    {
        Name = item.Name;
        Summary = item.Summary;
        IconUrl = item.IconUrl;
        Metadata = item.Metadata;
        Tags = item.Tags;
        Target = item.Target;
        OnPropertyChanged(nameof(HasIcon));
        OnPropertyChanged(nameof(Tags));
        IsFavorite = item.IsFavorite;
    }

    private static FavoriteEdition GetEdition(ResourceDefinition definition)
    {
        return definition.Kind is
            ResourceKind.BedrockBehaviorPack or ResourceKind.BedrockResourcePack
            or ResourceKind.BedrockWorld or ResourceKind.BedrockWorldTemplate
            ? FavoriteEdition.Bedrock
            : FavoriteEdition.Java;
    }
}

public sealed record JavaResourceSearchSource(string DisplayName, SearchSource Kind);

public sealed record JavaResourceSearchRequest(
    ResourceKind Kind,
    SearchSource Source,
    string Query,
    string GameVersion,
    ModLoaderType Loader,
    string IncludedCategories,
    string ExcludedCategories,
    ResourceEnvironment Environment,
    SearchSort Sort,
    int Page);

public sealed record JavaResourceSearchPage(IReadOnlyList<JavaResourceSearchResultItem> Items, int TotalCount);

public sealed class ModpackSearchPageViewModel() : JavaResourceSearchViewModel(ResourceDefinitions.Modpack);

public sealed class ResourcePackSearchPageViewModel()
    : JavaResourceSearchViewModel(ResourceDefinitions.ResourcePack);

public sealed class ShaderPackSearchPageViewModel() : JavaResourceSearchViewModel(ResourceDefinitions.ShaderPack);

public sealed class DataPackSearchPageViewModel() : JavaResourceSearchViewModel(ResourceDefinitions.DataPack);

public sealed class SaveSearchPageViewModel() : JavaResourceSearchViewModel(ResourceDefinitions.Save);

public sealed class BedrockResourceSearchViewModel(ResourceDefinition definition)
    : JavaResourceSearchViewModel(definition);

internal sealed class ApplicationCache<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _entries = new();
    private readonly Dictionary<TKey, Task<TValue>> _pending = new();
    private readonly object _lock = new();

    public bool TryGetValue(TKey key, out TValue? value)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(key, out value);
        }
    }

    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            _entries[key] = value;
        }
    }

    public Task<TValue> GetOrCreateAsync(TKey key, Func<Task<TValue>> factory)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var value)) return Task.FromResult(value);
            if (_pending.TryGetValue(key, out var pending)) return pending;

            var task = factory();
            _pending[key] = task;
            _ = CompleteAsync(key, task);
            return task;
        }
    }

    private async Task CompleteAsync(TKey key, Task<TValue> task)
    {
        try
        {
            var value = await task;
            lock (_lock)
            {
                _entries[key] = value;
                _pending.Remove(key);
            }
        }
        catch
        {
            lock (_lock)
            {
                _pending.Remove(key);
            }
        }
    }
}

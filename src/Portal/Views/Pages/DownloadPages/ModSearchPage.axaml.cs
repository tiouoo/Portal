using System.Collections.ObjectModel;
using AsyncImageLoader;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
using Portal.Module.Imaging;
using Portal.Localization;
using Portal.Views.Pages.InstancePages;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Views.Pages.DownloadPages;

public partial class ModSearchPage : UserControl
{
    public ModSearchPage()
    {
        InitializeComponent();
        DataContext = new ModSearchPageViewModel();
        Loaded += async (_, _) => await ((ModSearchPageViewModel)DataContext).InitializeAsync();
    }

    private void SearchBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ModSearchPageViewModel viewModel)
            return;

        viewModel.SearchCommand.Execute(null);
        e.Handled = true;
    }

    private void Result_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Control).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed ||
            (sender as Control)?.DataContext is not ModSearchResultItem item ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        ResourceDetailsPage.Open(topLevel, item.Target, item.FriendlyName);
        e.Handled = true;
    }

    private void Favorite_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is ModSearchResultItem item)
        {
            var resource = FavoriteResourceFactory.From(item);
            if (item.IsFavorite) FavoriteCollectionService.Instance.Remove(resource);
            else FavoriteCollectionService.Instance.Add(resource);
            item.IsFavorite = !item.IsFavorite;
        }

        e.Handled = true;
    }

    private async void Download_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is ModSearchResultItem item && TopLevel.GetTopLevel(this) is { } topLevel)
            await ResourceDownload.QuickDownloadAsync(topLevel, item.Target);
        e.Handled = true;
    }

    private void ShowDetails_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is ModSearchResultItem item && TopLevel.GetTopLevel(this) is { } topLevel)
            ResourceDetailsPage.Open(topLevel, item.Target, item.FriendlyName);
        e.Handled = true;
    }
}

public partial class ModSearchPageViewModel : ObservableObject, IDisposable, ISearchPageViewModel
{
    private const int PageSize = 40;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private bool _disposed;
    private CancellationTokenSource? _filterDebounce;
    private CancellationTokenSource? _gameVersionDebounce;
    private bool _initialized;
    private bool _suppressFilterSearch;

    public ModSearchPageViewModel()
    {
        FilterOptions = ResourceFilterCatalog.Get(ResourceKind.Mod)
            .Select(category => new ResourceFilterOption(category.Name, category.Id, OnFilterChanged)).ToArray();
        SelectedSource = Sources.FirstOrDefault(source => source.Kind == DownloadSearchPersistence.ToUiSource(Data.ConfigEntry.DefaultDownloadSearchSource))
                         ?? Sources[0];
        SelectedLoader = Loaders.FirstOrDefault(loader => loader.Kind == Data.ConfigEntry.DownloadSearchLoader)
                         ?? Loaders[0];
        SelectedSort = SortOptions.FirstOrDefault(sort => sort.Kind == DownloadSearchPersistence.ToUiSort(Data.ConfigEntry.DefaultDownloadSearchSort))
                       ?? SortOptions[0];
    }

    public ObservableCollection<ModSearchResultItem> Results { get; } = [];
    public ObservableCollection<string> MinecraftVersions { get; } = [];
    public IReadOnlyList<ResourceFilterOption> FilterOptions { get; }

    public IReadOnlyList<ModSearchSource> Sources { get; } =
    [
        new(CommonLanguageManager.Instance.mod_all.CurrentValue(), SearchSource.All),
        new("CurseForge", SearchSource.CurseForge),
        new("Modrinth", SearchSource.Modrinth)
    ];

    public string FilterText => $"{CommonLanguageManager.Instance.mod_filterButton.CurrentValue()}" +
                                (ActiveFilterCount > 0 ? $" ({ActiveFilterCount})" : string.Empty);
    public int ActiveFilterCount => FilterOptions.Count(option => option.IsSelected != false) +
                                    (SelectedEnvironment?.Kind == ResourceEnvironment.Any ? 0 : 1);
    public string CategoryTitle => CommonLanguageManager.Instance.mod_filterCategories.CurrentValue();
    public string ExcludeText => CommonLanguageManager.Instance.mod_filterExclude.CurrentValue();
    public string ClearText => CommonLanguageManager.Instance.mod_filterClear.CurrentValue();
    public string InvertText => CommonLanguageManager.Instance.mod_filterInvert.CurrentValue();
    public string ModrinthOnlyText => CommonLanguageManager.Instance.mod_filterModrinthOnly.CurrentValue();
    public bool CanUseAdvancedFilters => SelectedSource?.Kind != SearchSource.CurseForge;

    public IReadOnlyList<ModSearchLoader> Loaders { get; } =
    [
        new(CommonLanguageManager.Instance.mod_allLoaders.CurrentValue(), ModLoaderType.Any),
        new("Forge", ModLoaderType.Forge), new("NeoForge", ModLoaderType.NeoForge),
        new("Fabric", ModLoaderType.Fabric), new("Quilt", ModLoaderType.Quilt)
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

    [ObservableProperty] public partial ModSearchSource? SelectedSource { get; set; }
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
    public bool IsLoadingPlaceholder => IsLoading && Results.Count == 0;
    public bool IsEmpty => !IsLoading && !HasError && Results.Count == 0;
    public string LoadingText => CommonLanguageManager.Instance.modSearch_loading.CurrentValue();
    public string EmptyText => CommonLanguageManager.Instance.modSearch_noResults.CurrentValue();

    partial void OnIsLoadingChanged(bool value)
    {
        NotifyResultState();
    }

    partial void OnHasErrorChanged(bool value)
    {
        NotifyResultState();
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
        ScheduleFilterSearch();
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        _ = LoadVersionsAsync();

        _ = SearchAsync(true);
        await Task.CompletedTask;
    }

    partial void OnSelectedSourceChanged(ModSearchSource? value)
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
            }
            SelectedEnvironment = Environments[0];
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

        if (!_initialized)
            return;

        ScheduleFilterSearch();
    }

    private void OnFilterChanged()
    {
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(FilterText));
        if (!_initialized || _suppressFilterSearch)
            return;

        ScheduleFilterSearch();
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

        if (CurrentPage != 1)
        {
            CurrentPage = 1;
            return;
        }

        _ = SearchAsync(string.IsNullOrWhiteSpace(SearchText));
    }

    partial void OnGameVersionChanged(string value)
    {
        if (!_initialized || _suppressFilterSearch)
            return;

        ScheduleGameVersionSearch();
    }

    partial void OnCurrentPageChanged(int value)
    {
        if (_initialized && value > 0)
            _ = SearchAsync(false);
    }

    partial void OnSelectedLoaderChanged(ModSearchLoader? value)
    {
        if (_initialized)
            Data.ConfigEntry.DownloadSearchLoader = value?.Kind ?? ModLoaderType.Any;

        if (!_initialized || _suppressFilterSearch)
            return;

        if (CurrentPage != 1)
        {
            CurrentPage = 1;
            return;
        }

        _ = SearchAsync(string.IsNullOrWhiteSpace(SearchText));
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

    private void RestartSearch()
    {
        if (CurrentPage != 1)
        {
            CurrentPage = 1;
            return;
        }

        _ = SearchAsync(string.IsNullOrWhiteSpace(SearchText));
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

    private async Task SearchAsync(bool isDefaultSearch = false)
    {
        if (SelectedSource is null || SelectedSort is null) return;
        var includedCategories = string.Join('|', FilterOptions.Where(option => option.IsSelected == true)
            .Select(option => option.Id).Order());
        var excludedCategories = string.Join('|', FilterOptions.Where(option => option.IsSelected is null)
            .Select(option => option.Id).Order());
        var request = new SearchRequest(SelectedSource.Kind, SearchText.Trim(), GameVersion.Trim(),
            SelectedLoader?.Kind ?? ModLoaderType.Any, includedCategories, excludedCategories,
            SelectedEnvironment?.Kind ?? ResourceEnvironment.Any, SelectedSort.Kind, CurrentPage);
        var renderedCache = false;
        if (ModSearchCache.TryGetValue(request, out var cached) && cached is not null && IsCurrent(request))
        {
            Apply(cached.ToPageData());
            renderedCache = true;
        }

        if (IsCurrent(request))
        {
            HasError = false;
            StatusText = isDefaultSearch
                ? CommonLanguageManager.Instance.modSearch_fetchingPopular.CurrentValue()
                : CommonLanguageManager.Instance.modSearch_searching.CurrentValue();
        }

        if (IsCurrent(request) && !renderedCache)
            IsLoading = true;

        try
        {
            var page = await FetchAsync(request, _disposeCancellation.Token);

            ModSearchCache.Set(request, CachedSearchPage.From(page));
            if (IsCurrent(request)) Apply(page, renderedCache);
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!IsCurrent(request)) return;
            IsLoading = false;
            HasError = true;
            StatusText = CommonLanguageManager.Instance.modSearch_networkError.CurrentValue();
        }
    }

    private static async Task<SearchPageData> FetchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var includedTags = ResourceFilterCatalog.Parse(request.IncludedCategories, ResourceType.Mod);
        var excludedTags = ResourceFilterCatalog.Parse(request.ExcludedCategories, ResourceType.Mod);
        var source = ResourceFilterCatalog.ResolveSource(request.Source, includedTags, excludedTags, request.Environment);
        var options = new ResourceSearchOptions
        {
            Source = source,
            Type = ResourceType.Mod,
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
        return new SearchPageData(
            translated.Select(hit => new ModSearchResultItem(hit, request.Sort, request.GameVersion, request.Loader))
                .ToList(),
            page.TotalCount);
    }

    private void Apply(SearchPageData page, bool preserveExistingItems = false)
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
            ? CommonLanguageManager.Instance.modSearch_noResults.CurrentValue()
            : string.Format(CommonLanguageManager.Instance.modSearch_resultCount.CurrentValue(), page.TotalCount);
        NotifyResultState();
    }

    private bool IsCurrent(SearchRequest request)
    {
        return !_disposed && SelectedSource?.Kind == request.Source &&
               SearchText.Trim() == request.Query && GameVersion.Trim() == request.GameVersion &&
               (SelectedLoader?.Kind ?? ModLoaderType.Any) == request.Loader &&
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
            Logger.Debug($"[ModSearch] Version loading cancelled because the page closed: {exception}");
        }
        catch (Exception exception)
        {
            Logger.Warning($"[ModSearch] Version loading failed: {exception}");
        }
    }
}

public enum SearchSource
{
    CurseForge,
    Modrinth,
    All
}

public enum SearchSort
{
    Relevance,
    Popularity,
    Updated,
    Newest
}

public sealed record ModSearchLoader(string DisplayName, ModLoaderType Kind);

public sealed record ModSearchSort(string DisplayName, SearchSort Kind);

public sealed record ModSearchEnvironment(string DisplayName, ResourceEnvironment Kind);

public sealed record ModSearchSource(string DisplayName, SearchSource Kind);

public sealed partial class ModSearchResultItem : ObservableObject
{
    public ModSearchResultItem(ResourceHit hit, SearchSort sort = SearchSort.Relevance, string gameVersion = "",
        ModLoaderType loader = ModLoaderType.Any)
    {
        Name = hit.Title ?? string.Empty;
        FriendlyName = WikiEntries.FindChineseName(hit.Slug ?? string.Empty) ?? hit.Title ?? string.Empty;
        var summary = hit.Translation ?? hit.Summary ?? string.Empty;
        Summary = summary;
        Tags = ResourceSearchPresentation.BuildTags(hit);
        var timestamp = (sort is SearchSort.Newest ? hit.DateCreated : hit.DateModified) ??
                        hit.DateModified ?? hit.DateCreated;
        IconUrl = hit.IconUrl;
        Metadata = ResourceSearchPresentation.FormatMetadata(timestamp ?? default, hit.Downloads);
        Target = new ResourceDetailsTarget(ResourceDefinitions.Mod, ToModDetailsSource(hit.Source), hit.Id, gameVersion,
            loader);
        IsFavorite = FavoriteCollectionService.Instance.Contains(FavoriteResourceFactory.From(this));
    }

    internal ModSearchResultItem(CachedSearchItem item)
    {
        Name = item.Name;
        FriendlyName = item.FriendlyName;
        Summary = item.Summary;
        IconUrl = item.IconUrl;
        Metadata = item.Metadata;
        Tags = item.Tags;
        Target = item.Target;
        IsFavorite = FavoriteCollectionService.Instance.Contains(FavoriteResourceFactory.From(this));
    }

    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial string FriendlyName { get; set; }
    [ObservableProperty] public partial string Summary { get; set; }
    [ObservableProperty] public partial string? IconUrl { get; set; }
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconUrl);
    [ObservableProperty] public partial bool IsFavorite { get; set; }
    [ObservableProperty] public partial string Metadata { get; set; }
    public IReadOnlyList<string> Tags { get; private set; }
    public IAsyncImageLoader ImageLoader { get; } = new ModImageLoader();

    public ResourceDetailsTarget Target { get; private set; }

    public void Update(ModSearchResultItem item)
    {
        Name = item.Name;
        FriendlyName = item.FriendlyName;
        Summary = item.Summary;
        IconUrl = item.IconUrl;
        Metadata = item.Metadata;
        Tags = item.Tags;
        Target = item.Target;
        OnPropertyChanged(nameof(HasIcon));
        OnPropertyChanged(nameof(Tags));
    }

    internal static ModDetailsSource ToModDetailsSource(ResourceSource source)
    {
        return source == ResourceSource.Modrinth ? ModDetailsSource.Modrinth : ModDetailsSource.CurseForge;
    }
}

public sealed record SearchRequest(
    SearchSource Source,
    string Query,
    string GameVersion,
    ModLoaderType Loader,
    string IncludedCategories,
    string ExcludedCategories,
    ResourceEnvironment Environment,
    SearchSort Sort,
    int Page);

public sealed record SearchPageData(IReadOnlyList<ModSearchResultItem> Items, int TotalCount);

internal static class ModSearchCache
{
    private static readonly BoundedCache<SearchRequest, CachedSearchPage> Entries = new(32);

    public static bool TryGetValue(SearchRequest request, out CachedSearchPage? page)
    {
        return Entries.TryGetValue(request, out page);
    }

    public static void Set(SearchRequest request, CachedSearchPage page)
    {
        Entries.Set(request, page);
    }
}

internal sealed record CachedSearchItem(
    string Name,
    string FriendlyName,
    string Summary,
    string? IconUrl,
    string Metadata,
    IReadOnlyList<string> Tags,
    ResourceDetailsTarget Target);

internal sealed record CachedSearchPage(IReadOnlyList<CachedSearchItem> Items, int TotalCount)
{
    public static CachedSearchPage From(SearchPageData page)
    {
        return new CachedSearchPage(page.Items
            .Select(item => new CachedSearchItem(item.Name, item.FriendlyName, item.Summary, item.IconUrl,
                item.Metadata, item.Tags, item.Target)).ToList(), page.TotalCount);
    }

    public SearchPageData ToPageData()
    {
        return new SearchPageData(Items.Select(item => new ModSearchResultItem(item)).ToList(), TotalCount);
    }
}

using System.Collections.ObjectModel;
using AsyncImageLoader;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iridium.Enums;
using Iridium.Resources.Models;
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

        ModDetailsPage.Open(topLevel, item.Target, item.FriendlyName);
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
            await ModDetailsPage.QuickDownloadAsync(topLevel, item.Target);
        e.Handled = true;
    }

    private void ShowDetails_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is ModSearchResultItem item && TopLevel.GetTopLevel(this) is { } topLevel)
            ModDetailsPage.Open(topLevel, item.Target, item.FriendlyName);
        e.Handled = true;
    }
}

public partial class ModSearchPageViewModel : ObservableObject, IDisposable, ISearchPageViewModel
{
    private const int PageSize = 40;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private bool _disposed;
    private CancellationTokenSource? _gameVersionDebounce;
    private bool _initialized;
    private bool _suppressFilterSearch;

    public ModSearchPageViewModel()
    {
        SelectedSource = Sources.FirstOrDefault(source => source.Kind == DownloadSearchPersistence.ToUiSource(Data.ConfigEntry.DefaultDownloadSearchSource))
                         ?? Sources[0];
        SelectedLoader = Loaders.FirstOrDefault(loader => loader.Kind == Data.ConfigEntry.DownloadSearchLoader)
                         ?? Loaders[0];
        SelectedSort = SortOptions.FirstOrDefault(sort => sort.Kind == DownloadSearchPersistence.ToUiSort(Data.ConfigEntry.DefaultDownloadSearchSort))
                       ?? SortOptions[0];
    }

    public ObservableCollection<ModSearchResultItem> Results { get; } = [];
    public ObservableCollection<string> MinecraftVersions { get; } = [];

    public IReadOnlyList<ModSearchSource> Sources { get; } =
    [
        new(CommonLanguageManager.Instance.mod_all.CurrentValue(), SearchSource.All),
        new("CurseForge", SearchSource.CurseForge),
        new("Modrinth", SearchSource.Modrinth)
    ];

    public IReadOnlyList<ModSearchCategory> Categories => SelectedSource?.Categories ?? [];

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

    [ObservableProperty] public partial ModSearchSource? SelectedSource { get; set; }
    [ObservableProperty] public partial ModSearchCategory? SelectedCategory { get; set; }
    [ObservableProperty] public partial ModSearchLoader? SelectedLoader { get; set; }
    [ObservableProperty] public partial ModSearchSort? SelectedSort { get; set; }
    [ObservableProperty] public partial string GameVersion { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusText { get; set; } =
        CommonLanguageManager.Instance.modSearch_preparingSearch.CurrentValue();
    [ObservableProperty] public partial bool HasError { get; set; }
    [ObservableProperty] public partial int CurrentPage { get; set; } = 1;
    [ObservableProperty] public partial int TotalCount { get; set; }
    public bool HasResults => Results.Count > 0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _suppressFilterSearch = true;
        _gameVersionDebounce?.Cancel();
        _disposeCancellation.Cancel();
        Results.Clear();
        MinecraftVersions.Clear();
    }

    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;

    public void ExecuteSearch()
    {
        SearchCommand.Execute(null);
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

        OnPropertyChanged(nameof(Categories));
        _suppressFilterSearch = true;
        try
        {
            SelectedCategory = value?.Categories.FirstOrDefault();
            SelectedSort = SortOptions[0];
            GameVersion = string.Empty;
            SelectedLoader = Loaders[0];
        }
        finally
        {
            _suppressFilterSearch = false;
        }

        if (!_initialized)
            return;

        if (CurrentPage != 1)
        {
            CurrentPage = 1;
            return;
        }

        _ = SearchAsync(string.IsNullOrWhiteSpace(SearchText));
    }

    partial void OnSelectedCategoryChanged(ModSearchCategory? value)
    {
        if (!_initialized || _suppressFilterSearch)
            return;

        if (CurrentPage != 1)
        {
            CurrentPage = 1;
            return;
        }

        _ = SearchAsync(string.IsNullOrWhiteSpace(SearchText));
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
        var request = new SearchRequest(SelectedSource.Kind, SearchText.Trim(), GameVersion.Trim(),
            SelectedLoader?.Kind ?? ModLoaderType.Any, SelectedCategory?.Id ?? "", SelectedSort.Kind, CurrentPage);
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
            HasError = true;
            StatusText = CommonLanguageManager.Instance.modSearch_networkError.CurrentValue();
        }
    }

    private static async Task<SearchPageData> FetchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var options = new ResourceSearchOptions
        {
            Source = DownloadSearchPersistence.ToResourceSource(request.Source),
            Type = ResourceType.Mod,
            Query = string.IsNullOrWhiteSpace(request.Query) ? null : request.Query,
            GameVersion = string.IsNullOrWhiteSpace(request.GameVersion) ? null : request.GameVersion,
            Loader = request.Loader.ToResourceLoader(),
            Tags = BuildCategoryTags(request.Source, request.Category),
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

    private static IReadOnlyList<ResourceCategory> BuildCategoryTags(SearchSource source, string category)
    {
        if (string.IsNullOrWhiteSpace(category)) return [];
        if (source == SearchSource.Modrinth)
            return [new ResourceCategory { Type = ResourceType.Mod, ModrinthSlug = category }];
        return int.TryParse(category, out var categoryId) && categoryId > 0
            ? [new ResourceCategory { Type = ResourceType.Mod, CurseForgeId = categoryId }]
            : [];
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
        StatusText = page.TotalCount == 0
            ? CommonLanguageManager.Instance.modSearch_noResults.CurrentValue()
            : string.Format(CommonLanguageManager.Instance.modSearch_resultCount.CurrentValue(), page.TotalCount);
        OnPropertyChanged(nameof(HasResults));
    }

    private bool IsCurrent(SearchRequest request)
    {
        return !_disposed && SelectedSource?.Kind == request.Source &&
               SearchText.Trim() == request.Query && GameVersion.Trim() == request.GameVersion &&
               (SelectedLoader?.Kind ?? ModLoaderType.Any) == request.Loader &&
               (SelectedCategory?.Id ?? "") == request.Category &&
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

public sealed record ModSearchCategory(string DisplayName, string Id);

public sealed record ModSearchLoader(string DisplayName, ModLoaderType Kind);

public sealed record ModSearchSort(string DisplayName, SearchSort Kind);

public sealed record ModSearchSource(string DisplayName, SearchSource Kind)
{
    public IReadOnlyList<ModSearchCategory> Categories { get; } = Kind switch
    {
        SearchSource.All =>
        [
            new ModSearchCategory(CommonLanguageManager.Instance.mod_all.CurrentValue(), "")
        ],
        SearchSource.Modrinth =>
        [
            new ModSearchCategory(CommonLanguageManager.Instance.mod_all.CurrentValue(), ""),
            new ModSearchCategory(CommonLanguageManager.Instance.mod_catAdventure.CurrentValue(), "adventure"),
            new ModSearchCategory(CommonLanguageManager.Instance.mod_catEquipment.CurrentValue(), "equipment"),
            new ModSearchCategory(CommonLanguageManager.Instance.mod_catCursed.CurrentValue(), "cursed"),
            new ModSearchCategory(CommonLanguageManager.Instance.mod_catMagic.CurrentValue(), "magic"),
            new ModSearchCategory(CommonLanguageManager.Instance.mod_catUtility.CurrentValue(), "utility"),
            new ModSearchCategory(CommonLanguageManager.Instance.mod_catOptimization.CurrentValue(), "optimization"),
            new ModSearchCategory(CommonLanguageManager.Instance.mod_catWorldgen.CurrentValue(), "worldgen"),
            new ModSearchCategory(CommonLanguageManager.Instance.mod_catTechnology.CurrentValue(), "technology")
        ],
        _ =>
        [
            new ModSearchCategory(CommonLanguageManager.Instance.mod_all.CurrentValue(), "0"),
            new ModSearchCategory(CommonLanguageManager.Instance.mod_catAdventureExploration.CurrentValue(), "425"),
            new ModSearchCategory(CommonLanguageManager.Instance.mod_catArmorWeaponsTools.CurrentValue(), "406"),
            new ModSearchCategory(CommonLanguageManager.Instance.mod_catMagic.CurrentValue(), "5191"),
            new ModSearchCategory(CommonLanguageManager.Instance.mod_catTechnology.CurrentValue(), "412"),
            new ModSearchCategory(CommonLanguageManager.Instance.mod_catRedstone.CurrentValue(), "4558"),
            new ModSearchCategory(CommonLanguageManager.Instance.mod_catMapsInfo.CurrentValue(), "423"),
            new ModSearchCategory(CommonLanguageManager.Instance.mod_catPerformance.CurrentValue(), "6821"),
            new ModSearchCategory(CommonLanguageManager.Instance.mod_catApiLibraries.CurrentValue(), "421")
        ]
    };
}

public sealed partial class ModSearchResultItem : ObservableObject
{
    public ModSearchResultItem(ResourceHit hit, SearchSort sort = SearchSort.Relevance, string gameVersion = "",
        ModLoaderType loader = ModLoaderType.Any)
    {
        Name = hit.Title ?? string.Empty;
        FriendlyName = WikiEntries.FindChineseName(hit.Slug ?? string.Empty) ?? hit.Title ?? string.Empty;
        var sourceTag = DownloadSearchPersistence.SourceAbbreviation(hit.Source);
        var summary = hit.Translation ?? hit.Summary ?? string.Empty;
        Summary = string.IsNullOrEmpty(summary) ? sourceTag : $"{sourceTag}·{summary}";
        var timestamp = (sort is SearchSort.Newest ? hit.DateCreated : hit.DateModified) ??
                        hit.DateModified ?? hit.DateCreated;
        IconUrl = hit.IconUrl;
        Metadata = string.Format(CommonLanguageManager.Instance.mod_downloadCount.CurrentValue(),
            RelativeTime.Format(timestamp ?? default), hit.Downloads);
        Target = new ModDetailsTarget(ToModDetailsSource(hit.Source), hit.Id, gameVersion, loader);
        IsFavorite = FavoriteCollectionService.Instance.Contains(FavoriteResourceFactory.From(this));
    }

    internal ModSearchResultItem(CachedSearchItem item)
    {
        Name = item.Name;
        FriendlyName = item.FriendlyName;
        Summary = item.Summary;
        IconUrl = item.IconUrl;
        Metadata = item.Metadata;
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
    public IAsyncImageLoader ImageLoader { get; } = new ModImageLoader();

    public ModDetailsTarget Target { get; private set; }

    public void Update(ModSearchResultItem item)
    {
        Name = item.Name;
        FriendlyName = item.FriendlyName;
        Summary = item.Summary;
        IconUrl = item.IconUrl;
        Metadata = item.Metadata;
        Target = item.Target;
        OnPropertyChanged(nameof(HasIcon));
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
    string Category,
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
    ModDetailsTarget Target);

internal sealed record CachedSearchPage(IReadOnlyList<CachedSearchItem> Items, int TotalCount)
{
    public static CachedSearchPage From(SearchPageData page)
    {
        return new CachedSearchPage(page.Items
            .Select(item => new CachedSearchItem(item.Name, item.FriendlyName, item.Summary, item.IconUrl,
                item.Metadata, item.Target)).ToList(), page.TotalCount);
    }

    public SearchPageData ToPageData()
    {
        return new SearchPageData(Items.Select(item => new ModSearchResultItem(item)).ToList(), TotalCount);
    }
}

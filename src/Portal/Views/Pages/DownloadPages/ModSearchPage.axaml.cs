using System.Collections.ObjectModel;
using AsyncImageLoader;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Provider;
using Portal.Core.App.Helpers;
using Portal.Core.Const;
using Portal.Core.Minecraft.Models;
using Portal.Core.Minecraft.Services;
using Portal.Core.Services;
using Portal.Module.Imaging;
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
}

public partial class ModSearchPageViewModel : ObservableObject, IDisposable, ISearchPageViewModel
{
    private const int PageSize = 40;
    private readonly CurseforgeProvider _curseForge = new();
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly ModrinthProvider _modrinth = new();
    private bool _disposed;
    private bool _initialized;

    public ModSearchPageViewModel()
    {
        SelectedSource = Sources[1];
        SelectedLoader = Loaders[0];
        SelectedSort = SortOptions[0];
    }

    public ObservableCollection<ModSearchResultItem> Results { get; } = [];
    public ObservableCollection<string> MinecraftVersions { get; } = [];

    public IReadOnlyList<ModSearchSource> Sources { get; } =
        [new("CurseForge", SearchSource.CurseForge), new("Modrinth", SearchSource.Modrinth)];

    public IReadOnlyList<ModSearchCategory> Categories => SelectedSource?.Categories ?? [];

    public IReadOnlyList<ModSearchLoader> Loaders { get; } =
    [
        new("全部加载器", ModLoaderType.Any), new("Forge", ModLoaderType.Forge), new("NeoForge", ModLoaderType.NeoForge),
        new("Fabric", ModLoaderType.Fabric), new("Quilt", ModLoaderType.Quilt)
    ];

    public IReadOnlyList<ModSearchSort> SortOptions { get; } =
    [
        new("相关度", SearchSort.Relevance), new("热度", SearchSort.Popularity), new("最近更新", SearchSort.Updated),
        new("最新发布", SearchSort.Newest)
    ];

    [ObservableProperty] public partial ModSearchSource? SelectedSource { get; set; }
    [ObservableProperty] public partial ModSearchCategory? SelectedCategory { get; set; }
    [ObservableProperty] public partial ModSearchLoader? SelectedLoader { get; set; }
    [ObservableProperty] public partial ModSearchSort? SelectedSort { get; set; }
    [ObservableProperty] public partial string GameVersion { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusText { get; set; } = "准备搜索...";
    [ObservableProperty] public partial bool HasError { get; set; }
    [ObservableProperty] public partial int CurrentPage { get; set; } = 1;
    [ObservableProperty] public partial int TotalCount { get; set; }
    public bool HasResults => Results.Count > 0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
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
        OnPropertyChanged(nameof(Categories));
        SelectedCategory = value?.Categories.FirstOrDefault();
        SelectedSort = SortOptions[0];
        GameVersion = string.Empty;
        SelectedLoader = Loaders[0];

        if (!_initialized)
            return;

        if (CurrentPage != 1)
        {
            CurrentPage = 1;
            return;
        }

        _ = SearchAsync(string.IsNullOrWhiteSpace(SearchText));
    }

    partial void OnCurrentPageChanged(int value)
    {
        if (_initialized && value > 0)
            _ = SearchAsync();
    }

    partial void OnSelectedLoaderChanged(ModSearchLoader? value)
    {
        if (!_initialized)
            return;

        if (CurrentPage != 1)
        {
            CurrentPage = 1;
            return;
        }

        _ = SearchAsync(string.IsNullOrWhiteSpace(SearchText));
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
        if (isDefaultSearch)
        {
            ModSearchCache.TryGetValue(request, out var cached);
            if (cached is not null && IsCurrent(request))
            {
                Apply(cached.ToPageData());
                renderedCache = true;
            }
        }

        if (IsCurrent(request))
        {
            HasError = false;
            StatusText = isDefaultSearch ? "正在获取热门模组..." : "正在搜索...";
        }

        try
        {
            var page = await FetchAsync(request, _disposeCancellation.Token);

            if (isDefaultSearch) ModSearchCache.Set(request, CachedSearchPage.From(page));
            if (IsCurrent(request)) Apply(page, renderedCache);
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!IsCurrent(request)) return;
            HasError = true;
            StatusText = "网络错误，无法完成搜索。";
        }
    }

    private async Task<SearchPageData> FetchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var offset = (request.Page - 1) * PageSize;
        if (request.Source is SearchSource.Modrinth)
        {
            var modrinthPage = await _modrinth.SearchPageAsync(request.Query, request.GameVersion, request.Category,
                modLoader: request.Loader, index: MinecraftVersionParsing.ToModrinthSort(request.Sort), offset: offset,
                limit: PageSize,
                cancellationToken: cancellationToken);
            var items = modrinthPage.Items.ToArray();
            var translations = await ProjectTranslationService.GetTranslationsAsync(ProjectTranslationSource.Modrinth,
                items.Select(item => item.ProjectId), cancellationToken);
            return new SearchPageData(items.Select(item => new ModSearchResultItem(
                translations.TryGetValue(item.ProjectId, out var translated)
                    ? item with { Summary = translated }
                    : item, request.Sort,
                request.GameVersion, request.Loader)).ToList(), modrinthPage.TotalCount);
        }

        var page = await _curseForge.SearchResourcesPageAsync(new CurseforgeSearchOptions
        {
            SearchFilter = request.Query,
            CategoryId = int.TryParse(request.Category, out var category) ? category : 0,
            GameVersion = string.IsNullOrWhiteSpace(request.GameVersion) ? null : request.GameVersion,
            ModLoaderType = request.Loader,
            SortField = MinecraftVersionParsing.ToCurseForgeSort(request.Sort),
            SortOrder = SortOrder.Desc,
            Index = offset,
            PageSize = PageSize
        }, cancellationToken);
        var curseForgeItems = page.Items.ToArray();
        var curseForgeTranslations = await ProjectTranslationService.GetTranslationsAsync(
            ProjectTranslationSource.CurseForge,
            curseForgeItems.Select(item => item.Id.ToString()), cancellationToken);
        return new SearchPageData(curseForgeItems.Select(item => new ModSearchResultItem(
            curseForgeTranslations.TryGetValue(item.Id.ToString(), out var translated)
                ? item with { Summary = translated }
                : item,
            request.GameVersion,
            request.Loader)).ToList(), page.TotalCount);
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
        StatusText = page.TotalCount == 0 ? "没有找到匹配的模组。" : $"共 {page.TotalCount} 个模组";
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
    Modrinth
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
    public IReadOnlyList<ModSearchCategory> Categories { get; } = Kind is SearchSource.Modrinth
        ?
        [
            new ModSearchCategory("全部", ""), new ModSearchCategory("冒险", "adventure"),
            new ModSearchCategory("装备", "equipment"), new ModSearchCategory("诅咒", "cursed"),
            new ModSearchCategory("生物魔法", "magic"), new ModSearchCategory("实用", "utility"),
            new ModSearchCategory("优化", "optimization"), new ModSearchCategory("世界生成", "worldgen"),
            new ModSearchCategory("科技", "technology")
        ]
        :
        [
            new ModSearchCategory("全部", "0"), new ModSearchCategory("冒险与探索", "425"),
            new ModSearchCategory("盔甲、武器与工具", "406"), new ModSearchCategory("魔法", "5191"),
            new ModSearchCategory("科技", "412"), new ModSearchCategory("红石", "4558"),
            new ModSearchCategory("地图与信息", "423"), new ModSearchCategory("性能优化", "6821"),
            new ModSearchCategory("API 与库", "421")
        ];
}

public sealed partial class ModSearchResultItem : ObservableObject
{
    public ModSearchResultItem(ModrinthResource item, SearchSort sort = SearchSort.Relevance, string gameVersion = "",
        ModLoaderType loader = ModLoaderType.Any)
    {
        Name = item.Name;
        FriendlyName = WikiEntries.FindChineseName(item.Slug) ?? item.Name;
        Summary = item.Summary;
        var timestamp = sort is SearchSort.Newest ? item.DateModified : item.Updated;
        IconUrl = item.IconUrl;
        Metadata = $"{RelativeTime.Format(timestamp)}·{item.DownloadCount:N0} 下载";
        Target = new ModDetailsTarget(ModDetailsSource.Modrinth, item.ProjectId, gameVersion, loader);
        IsFavorite = FavoriteCollectionService.Instance.Contains(FavoriteResourceFactory.From(this));
    }

    public ModSearchResultItem(CurseforgeResource item, string gameVersion = "",
        ModLoaderType loader = ModLoaderType.Any)
    {
        Name = item.Name;
        FriendlyName = WikiEntries.FindChineseName(item.Slug) ?? item.Name;
        Summary = item.Summary;
        IconUrl = item.IconUrl;
        Metadata = $"{RelativeTime.Format(item.DateModified)}·{item.DownloadCount:N0} 下载";
        Target = new ModDetailsTarget(ModDetailsSource.CurseForge, item.Id.ToString(), gameVersion, loader);
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
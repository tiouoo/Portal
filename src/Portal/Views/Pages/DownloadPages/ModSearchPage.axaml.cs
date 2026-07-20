using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using AsyncImageLoader;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Installer;
using MinecraftLaunch.Components.Provider;
using Portal.Const;
using Portal.Views.Pages.InstancePages;
using Portal.Views.Pages;

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
            (sender as Control)?.DataContext is not ModSearchResultItem item || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        ModDetailsPage.Open(topLevel, item.Target, item.FriendlyName);
        e.Handled = true;
    }
}

public partial class ModSearchPageViewModel : ObservableObject
{
    private const int PageSize = 40;
    private static readonly SemaphoreSlim VersionLoadLock = new(1, 1);
    private static Task<IReadOnlyList<MinecraftLaunch.Base.Models.Network.VersionManifestEntry>>? _versionLoadTask;
    private readonly ModrinthProvider _modrinth = new();
    private readonly CurseforgeProvider _curseForge = new();
    private bool _initialized;

    public ObservableCollection<ModSearchResultItem> Results { get; } = [];
    public ObservableCollection<string> MinecraftVersions { get; } = [];
    public IReadOnlyList<ModSearchSource> Sources { get; } =
        [new("CurseForge", SearchSource.CurseForge), new("Modrinth", SearchSource.Modrinth)];
    public IReadOnlyList<ModSearchCategory> Categories => SelectedSource?.Categories ?? [];
    public IReadOnlyList<ModSearchLoader> Loaders { get; } =
        [new("全部加载器", ModLoaderType.Any), new("Forge", ModLoaderType.Forge), new("NeoForge", ModLoaderType.NeoForge),
            new("Fabric", ModLoaderType.Fabric), new("Quilt", ModLoaderType.Quilt)];
    public IReadOnlyList<ModSearchSort> SortOptions { get; } =
        [new("相关度", SearchSort.Relevance), new("热度", SearchSort.Popularity), new("最近更新", SearchSort.Updated), new("最新发布", SearchSort.Newest)];

    [ObservableProperty] public partial ModSearchSource? SelectedSource { get; set; }
    [ObservableProperty] public partial ModSearchCategory? SelectedCategory { get; set; }
    [ObservableProperty] public partial ModSearchLoader? SelectedLoader { get; set; }
    [ObservableProperty] public partial ModSearchSort? SelectedSort { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial string GameVersion { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusText { get; set; } = "准备搜索...";
    [ObservableProperty] public partial bool HasError { get; set; }
    [ObservableProperty] public partial int CurrentPage { get; set; } = 1;
    [ObservableProperty] public partial int TotalCount { get; set; }
    public bool HasResults => Results.Count > 0;

    public ModSearchPageViewModel()
    {
        SelectedSource = Sources[1];
        SelectedLoader = Loaders[0];
        SelectedSort = SortOptions[0];
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        _ = LoadVersionsAsync();
        // Do not await this request: a later keyword search must not cancel a popular-results refresh.
        _ = SearchAsync(isDefaultSearch: true);
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
    private Task RetryAsync() => SearchAsync(string.IsNullOrWhiteSpace(SearchText));

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
            var page = await FetchAsync(request);
            // All empty-keyword searches, including filtered popular lists, are persisted.
            if (isDefaultSearch) ModSearchCache.Set(request, CachedSearchPage.From(page));
            if (IsCurrent(request)) Apply(page, preserveExistingItems: renderedCache);
        }
        catch (Exception)
        {
            if (!IsCurrent(request)) return;
            HasError = true;
            StatusText = "网络错误，无法完成搜索。";
        }
    }

    private async Task<SearchPageData> FetchAsync(SearchRequest request)
    {
        var offset = (request.Page - 1) * PageSize;
        if (request.Source is SearchSource.Modrinth)
        {
            var modrinthPage = await _modrinth.SearchPageAsync(request.Query, request.GameVersion, request.Category,
                modLoader: request.Loader, index: ToModrinthSort(request.Sort), offset: offset, limit: PageSize);
            return new SearchPageData(modrinthPage.Items.Select(item => new ModSearchResultItem(item, request.Sort,
                request.GameVersion, request.Loader)).ToList(), modrinthPage.TotalCount);
        }

        var page = await _curseForge.SearchResourcesPageAsync(new CurseforgeSearchOptions
        {
            SearchFilter = request.Query,
            CategoryId = int.TryParse(request.Category, out var category) ? category : 0,
            GameVersion = string.IsNullOrWhiteSpace(request.GameVersion) ? null : request.GameVersion,
            ModLoaderType = request.Loader,
            SortField = ToCurseForgeSort(request.Sort),
            SortOrder = SortOrder.Desc,
            Index = offset,
            PageSize = PageSize
        });
        return new SearchPageData(page.Items.Select(item => new ModSearchResultItem(item, request.GameVersion,
            request.Loader)).ToList(), page.TotalCount);
    }

    private void Apply(SearchPageData page, bool preserveExistingItems = false)
    {
        // The initial popular request refreshes cached cards in place. Replacing the collection
        // recreates AdvancedImage controls and makes already loaded icons visibly flicker.
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

    private bool IsCurrent(SearchRequest request) => SelectedSource?.Kind == request.Source &&
        SearchText.Trim() == request.Query && GameVersion.Trim() == request.GameVersion &&
        (SelectedLoader?.Kind ?? ModLoaderType.Any) == request.Loader && (SelectedCategory?.Id ?? "") == request.Category &&
        SelectedSort?.Kind == request.Sort && CurrentPage == request.Page;

    private async Task LoadVersionsAsync()
    {
        await VersionLoadLock.WaitAsync();
        try
        {
            var entries = Data.UiProperty.MinecraftVersionManifestEntries;
            if (_versionLoadTask is null)
                _versionLoadTask = entries.Count == 0
                    ? LoadReleaseManifestAsync()
                    : Task.FromResult<IReadOnlyList<MinecraftLaunch.Base.Models.Network.VersionManifestEntry>>(entries);
            var loadedEntries = await _versionLoadTask;
            if (entries.Count == 0) entries.AddRange(loadedEntries);
            var versions = entries.Where(x => x.Type == "release").Select(x => x.Id).Distinct()
                .OrderByDescending(ParseMinecraftVersion).ThenByDescending(x => x, StringComparer.Ordinal).ToList();
            MinecraftVersions.Clear();
            foreach (var version in versions) MinecraftVersions.Add(version);
        }
        catch (Exception) { }
        finally { VersionLoadLock.Release(); }
    }

    private static ModrinthSearchIndex ToModrinthSort(SearchSort sort) => sort switch
    {
        SearchSort.Popularity => ModrinthSearchIndex.Downloads, SearchSort.Updated => ModrinthSearchIndex.DateUpdated,
        SearchSort.Newest => ModrinthSearchIndex.DatePublished, _ => ModrinthSearchIndex.Relevance
    };

    private static SortField ToCurseForgeSort(SearchSort sort) => sort switch
    {
        SearchSort.Popularity => SortField.Popularity, SearchSort.Updated => SortField.LastUpdated,
        SearchSort.Newest => SortField.ReleasedDate, _ => SortField.Featured
    };

    private static async Task<IReadOnlyList<MinecraftLaunch.Base.Models.Network.VersionManifestEntry>> LoadReleaseManifestAsync() =>
        (await VanillaInstaller.EnumerableMinecraftAsync()).ToList();

    private static MinecraftVersionSortKey ParseMinecraftVersion(string value)
    {
        var match = System.Text.RegularExpressions.Regex.Match(value,
            @"^(?<major>\d+)\.(?<minor>\d+)(?:\.(?<patch>\d+))?(?<suffix>.*)$");
        if (!match.Success) return new MinecraftVersionSortKey(-1, -1, -1, -1);
        var suffix = match.Groups["suffix"].Value;
        // A release sorts ahead of release candidates, pre-releases and snapshots of the same version.
        var stage = string.IsNullOrEmpty(suffix) ? 3 : suffix.Contains("rc", StringComparison.OrdinalIgnoreCase) ? 2 :
            suffix.Contains("pre", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        return new MinecraftVersionSortKey(int.Parse(match.Groups["major"].Value), int.Parse(match.Groups["minor"].Value),
            match.Groups["patch"].Success ? int.Parse(match.Groups["patch"].Value) : 0, stage);
    }
}

public readonly record struct MinecraftVersionSortKey(int Major, int Minor, int Patch, int Stage) : IComparable<MinecraftVersionSortKey>
{
    public int CompareTo(MinecraftVersionSortKey other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        return result != 0 ? result : Stage.CompareTo(other.Stage);
    }
}

public enum SearchSource { CurseForge, Modrinth }
public enum SearchSort { Relevance, Popularity, Updated, Newest }
public sealed record ModSearchCategory(string DisplayName, string Id);
public sealed record ModSearchLoader(string DisplayName, ModLoaderType Kind);
public sealed record ModSearchSort(string DisplayName, SearchSort Kind);
public sealed record ModSearchSource(string DisplayName, SearchSource Kind)
{
    public IReadOnlyList<ModSearchCategory> Categories { get; } = Kind is SearchSource.Modrinth
        ? [new("全部", ""), new("冒险", "adventure"), new("装备", "equipment"), new("诅咒", "cursed"), new("生物魔法", "magic"), new("实用", "utility"), new("优化", "optimization"), new("世界生成", "worldgen"), new("科技", "technology")]
        : [new("全部", "0"), new("冒险与探索", "425"), new("盔甲、武器与工具", "406"), new("魔法", "5191"), new("科技", "412"), new("红石", "4558"), new("地图与信息", "423"), new("性能优化", "6821"), new("API 与库", "421")];
}

public sealed partial class ModSearchResultItem : ObservableObject
{
    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial string FriendlyName { get; set; }
    [ObservableProperty] public partial string Summary { get; set; }
    [ObservableProperty] public partial string? IconUrl { get; set; }
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconUrl);
    [ObservableProperty] public partial string Metadata { get; set; }
    public IAsyncImageLoader ImageLoader { get; } = new ModImageLoader();

    public ModDetailsTarget Target { get; private set; }

    public ModSearchResultItem(ModrinthResource item, SearchSort sort = SearchSort.Relevance, string gameVersion = "",
        ModLoaderType loader = ModLoaderType.Any)
    {
        Name = item.Name; FriendlyName = WikiEntries.FindChineseName(item.Slug) ?? item.Name; Summary = item.Summary;
        var timestamp = sort is SearchSort.Newest ? item.DateModified : item.Updated;
        IconUrl = item.IconUrl; Metadata = $"{FormatRelativeTime(timestamp)}·{item.DownloadCount:N0} 下载";
        Target = new ModDetailsTarget(ModDetailsSource.Modrinth, item.ProjectId, gameVersion, loader);
    }

    public ModSearchResultItem(CurseforgeResource item, string gameVersion = "", ModLoaderType loader = ModLoaderType.Any)
    {
        Name = item.Name; FriendlyName = WikiEntries.FindChineseName(item.Slug) ?? item.Name; Summary = item.Summary;
        IconUrl = item.IconUrl; Metadata = $"{FormatRelativeTime(item.DateModified)}·{item.DownloadCount:N0} 下载";
        Target = new ModDetailsTarget(ModDetailsSource.CurseForge, item.Id.ToString(), gameVersion, loader);
    }

    internal ModSearchResultItem(CachedSearchItem item)
    {
        Name = item.Name; FriendlyName = item.FriendlyName; Summary = item.Summary;
        IconUrl = item.IconUrl; Metadata = item.Metadata;
        Target = item.Target;
    }

    public void Update(ModSearchResultItem item)
    {
        Name = item.Name;
        FriendlyName = item.FriendlyName;
        Summary = item.Summary;
        IconUrl = item.IconUrl;
        Metadata = item.Metadata;
        Target = item.Target;
    }

    private static string FormatRelativeTime(DateTime timestamp)
    {
        var localTime = timestamp.Kind == DateTimeKind.Utc ? timestamp.ToLocalTime() : timestamp;
        var elapsed = DateTime.Now - localTime;
        if (elapsed < TimeSpan.Zero) return "刚刚";
        if (elapsed < TimeSpan.FromMinutes(1)) return "刚刚";
        if (elapsed < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)elapsed.TotalMinutes)} 分钟前";
        if (elapsed < TimeSpan.FromDays(1)) return $"{Math.Max(1, (int)elapsed.TotalHours)} 小时前";
        if (elapsed < TimeSpan.FromDays(2)) return "昨天";
        if (elapsed < TimeSpan.FromDays(7)) return $"{(int)elapsed.TotalDays} 天前";
        if (elapsed < TimeSpan.FromDays(14)) return "上周";
        if (elapsed < TimeSpan.FromDays(30)) return $"{Math.Max(2, (int)(elapsed.TotalDays / 7))} 周前";
        if (elapsed < TimeSpan.FromDays(365)) return $"{Math.Max(1, (int)(elapsed.TotalDays / 30))} 个月前";
        return $"{Math.Max(1, (int)(elapsed.TotalDays / 365))} 年前";
    }
}

public sealed record SearchRequest(SearchSource Source, string Query, string GameVersion, ModLoaderType Loader, string Category,
    SearchSort Sort, int Page);
public sealed record SearchPageData(IReadOnlyList<ModSearchResultItem> Items, int TotalCount);

// Search data is only reused while Portal is running. It is never persisted to disk.
internal static class ModSearchCache
{
    private static readonly ConcurrentDictionary<SearchRequest, CachedSearchPage> Entries = new();

    public static bool TryGetValue(SearchRequest request, out CachedSearchPage? page) =>
        Entries.TryGetValue(request, out page);

    public static void Set(SearchRequest request, CachedSearchPage page) => Entries[request] = page;
}

internal sealed record CachedSearchItem(string Name, string FriendlyName, string Summary, string? IconUrl, string Metadata,
    ModDetailsTarget Target);
internal sealed record CachedSearchPage(IReadOnlyList<CachedSearchItem> Items, int TotalCount)
{
    public static CachedSearchPage From(SearchPageData page) => new(page.Items
        .Select(item => new CachedSearchItem(item.Name, item.FriendlyName, item.Summary, item.IconUrl, item.Metadata, item.Target)).ToList(), page.TotalCount);

    public SearchPageData ToPageData() => new(Items.Select(item => new ModSearchResultItem(item)).ToList(), TotalCount);
}

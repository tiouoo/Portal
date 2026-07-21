using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using AsyncImageLoader;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Installer;
using MinecraftLaunch.Components.Provider;
using Portal.Const;
using Portal.Views.Pages.InstancePages;

namespace Portal.Views.Pages.DownloadPages;

public enum JavaResourceKind
{
    Modpack,
    ResourcePack,
    ShaderPack,
    DataPack,
    Save,
    BedrockBehaviorPack,
    BedrockResourcePack,
    BedrockWorld,
    BedrockWorldTemplate
}

public sealed record JavaResourceDefinition(
    JavaResourceKind Kind,
    string DisplayName,
    string ProjectType,
    int? CurseForgeClassId,
    bool SupportsDownload,
    bool SupportsLoaderFilter,
    bool SupportsModrinth = true,
    int CurseForgeGameId = 432);

public static class JavaResourceDefinitions
{
    public static JavaResourceDefinition Modpack { get; } =
        new(JavaResourceKind.Modpack, "整合包", "modpack", 4471, false, true);
    public static JavaResourceDefinition ResourcePack { get; } =
        new(JavaResourceKind.ResourcePack, "材质包", "resourcepack", 12, true, false);
    public static JavaResourceDefinition ShaderPack { get; } =
        new(JavaResourceKind.ShaderPack, "光影包", "shader", 6552, true, false);
    public static JavaResourceDefinition DataPack { get; } =
        new(JavaResourceKind.DataPack, "数据包", "datapack", 6945, true, false);
    public static JavaResourceDefinition Save { get; } =
        new(JavaResourceKind.Save, "存档", "world", 17, true, false, false);
}

public abstract partial class JavaResourceSearchViewModel : ObservableObject
{
    private const int PageSize = 40;
    private static readonly SemaphoreSlim VersionLoadLock = new(1, 1);
    private static Task<IReadOnlyList<VersionManifestEntry>>? _versionLoadTask;
    private static readonly ConcurrentDictionary<JavaResourceSearchRequest, JavaResourceSearchPage> Cache = new();
    private readonly ModrinthProvider _modrinth = new();
    private readonly CurseforgeProvider _curseForge = new();
    private bool _initialized;

    public JavaResourceDefinition Definition { get; }
    public string PageTitle => $"{Definition.DisplayName}搜索";
    public string SearchPlaceholder => $"搜索{Definition.DisplayName}";
    public bool ShowLoaderFilter => Definition.SupportsLoaderFilter;
    public ObservableCollection<JavaResourceSearchResultItem> Results { get; } = [];
    public ObservableCollection<string> MinecraftVersions { get; } = [];
    public IReadOnlyList<JavaResourceSearchSource> Sources { get; }
    protected JavaResourceSearchViewModel(JavaResourceDefinition definition)
    {
        Definition = definition;
        Sources = definition.SupportsModrinth
            ? [new("CurseForge", SearchSource.CurseForge), new("Modrinth", SearchSource.Modrinth)]
            : [new("CurseForge", SearchSource.CurseForge)];
        SelectedSource = Sources.Last();
        SelectedLoader = Loaders[0];
        SelectedSort = SortOptions[0];
    }
    public IReadOnlyList<ModSearchLoader> Loaders { get; } =
        [new("全部加载器", ModLoaderType.Any), new("Forge", ModLoaderType.Forge),
            new("NeoForge", ModLoaderType.NeoForge), new("Fabric", ModLoaderType.Fabric),
            new("Quilt", ModLoaderType.Quilt)];
    public IReadOnlyList<ModSearchSort> SortOptions { get; } =
        [new("相关度", SearchSort.Relevance), new("热度", SearchSort.Popularity),
            new("最近更新", SearchSort.Updated), new("最新发布", SearchSort.Newest)];

    [ObservableProperty] public partial JavaResourceSearchSource? SelectedSource { get; set; }
    [ObservableProperty] public partial ModSearchLoader? SelectedLoader { get; set; }
    [ObservableProperty] public partial ModSearchSort? SelectedSort { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial string GameVersion { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusText { get; set; } = "准备搜索...";
    [ObservableProperty] public partial bool HasError { get; set; }
    [ObservableProperty] public partial int CurrentPage { get; set; } = 1;
    [ObservableProperty] public partial int TotalCount { get; set; }
    public bool HasResults => Results.Count > 0;

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
        SelectedSort = SortOptions[0];
        GameVersion = string.Empty;
        SelectedLoader = Loaders[0];
        RestartSearch();
    }

    partial void OnSelectedLoaderChanged(ModSearchLoader? value)
    {
        if (ShowLoaderFilter) RestartSearch();
    }

    partial void OnCurrentPageChanged(int value)
    {
        if (_initialized && value > 0) _ = SearchAsync();
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
        var request = new JavaResourceSearchRequest(Definition.Kind, SelectedSource.Kind, SearchText.Trim(),
            GameVersion.Trim(), ShowLoaderFilter ? SelectedLoader?.Kind ?? ModLoaderType.Any : ModLoaderType.Any,
            SelectedSort.Kind, CurrentPage);
        JavaResourceSearchPage? cached = null;
        var renderedCache = isDefaultSearch && Cache.TryGetValue(request, out cached);
        if (renderedCache && IsCurrent(request)) Apply(cached!);

        if (IsCurrent(request))
        {
            HasError = false;
            StatusText = isDefaultSearch ? $"正在获取热门{Definition.DisplayName}..." : "正在搜索...";
        }

        try
        {
            var page = await FetchAsync(request);
            if (isDefaultSearch) Cache[request] = page;
            if (IsCurrent(request)) Apply(page, renderedCache);
        }
        catch
        {
            if (!IsCurrent(request)) return;
            HasError = true;
            StatusText = "网络错误，无法完成搜索。";
        }
    }

    private async Task<JavaResourceSearchPage> FetchAsync(JavaResourceSearchRequest request)
    {
        var offset = (request.Page - 1) * PageSize;
        if (request.Source == SearchSource.Modrinth)
        {
            var page = await _modrinth.SearchPageAsync(request.Query, request.GameVersion,
                projectType: Definition.ProjectType, modLoader: request.Loader,
                index: ToModrinthSort(request.Sort), offset: offset, limit: PageSize);
            return new JavaResourceSearchPage(page.Items.Select(item =>
                new JavaResourceSearchResultItem(item, Definition, request.GameVersion, request.Loader)).ToArray(),
                page.TotalCount);
        }

        var curseForgePage = await _curseForge.SearchResourcesPageAsync(new CurseforgeSearchOptions
        {
            ClassId = Definition.CurseForgeClassId,
            GameId = Definition.CurseForgeGameId,
            SearchFilter = request.Query,
            GameVersion = string.IsNullOrWhiteSpace(request.GameVersion) ? null : request.GameVersion,
            ModLoaderType = request.Loader,
            SortField = ToCurseForgeSort(request.Sort),
            SortOrder = SortOrder.Desc,
            Index = offset,
            PageSize = PageSize
        });
        return new JavaResourceSearchPage(curseForgePage.Items.Select(item =>
            new JavaResourceSearchResultItem(item, Definition, request.GameVersion, request.Loader)).ToArray(),
            curseForgePage.TotalCount);
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
        StatusText = page.TotalCount == 0
            ? $"没有找到匹配的{Definition.DisplayName}。"
            : $"共 {page.TotalCount} 个{Definition.DisplayName}";
        OnPropertyChanged(nameof(HasResults));
    }

    private bool IsCurrent(JavaResourceSearchRequest request) => Definition.Kind == request.Kind &&
        SelectedSource?.Kind == request.Source && SearchText.Trim() == request.Query &&
        GameVersion.Trim() == request.GameVersion &&
        (ShowLoaderFilter ? SelectedLoader?.Kind ?? ModLoaderType.Any : ModLoaderType.Any) == request.Loader &&
        SelectedSort?.Kind == request.Sort && CurrentPage == request.Page;

    private async Task LoadVersionsAsync()
    {
        await VersionLoadLock.WaitAsync();
        try
        {
            var entries = Data.UiProperty.MinecraftVersionManifestEntries;
            _versionLoadTask ??= entries.Count == 0
                ? LoadReleaseManifestAsync()
                : Task.FromResult<IReadOnlyList<VersionManifestEntry>>(entries);
            var loadedEntries = await _versionLoadTask;
            if (entries.Count == 0) entries.AddRange(loadedEntries);
            var versions = entries.Where(x => x.Type == "release").Select(x => x.Id).Distinct()
                .OrderByDescending(ParseMinecraftVersion).ThenByDescending(x => x, StringComparer.Ordinal);
            MinecraftVersions.Clear();
            foreach (var version in versions) MinecraftVersions.Add(version);
        }
        catch
        {
        }
        finally
        {
            VersionLoadLock.Release();
        }
    }

    private static ModrinthSearchIndex ToModrinthSort(SearchSort sort) => sort switch
    {
        SearchSort.Popularity => ModrinthSearchIndex.Downloads,
        SearchSort.Updated => ModrinthSearchIndex.DateUpdated,
        SearchSort.Newest => ModrinthSearchIndex.DatePublished,
        _ => ModrinthSearchIndex.Relevance
    };

    private static SortField ToCurseForgeSort(SearchSort sort) => sort switch
    {
        SearchSort.Popularity => SortField.Popularity,
        SearchSort.Updated => SortField.LastUpdated,
        SearchSort.Newest => SortField.ReleasedDate,
        _ => SortField.Featured
    };

    private static async Task<IReadOnlyList<VersionManifestEntry>> LoadReleaseManifestAsync() =>
        (await VanillaInstaller.EnumerableMinecraftAsync()).ToList();

    private static MinecraftVersionSortKey ParseMinecraftVersion(string value)
    {
        var match = System.Text.RegularExpressions.Regex.Match(value,
            @"^(?<major>\d+)\.(?<minor>\d+)(?:\.(?<patch>\d+))?(?<suffix>.*)$");
        if (!match.Success) return new MinecraftVersionSortKey(-1, -1, -1, -1);
        var suffix = match.Groups["suffix"].Value;
        var stage = string.IsNullOrEmpty(suffix) ? 3 : suffix.Contains("rc", StringComparison.OrdinalIgnoreCase) ? 2 :
            suffix.Contains("pre", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        return new MinecraftVersionSortKey(int.Parse(match.Groups["major"].Value),
            int.Parse(match.Groups["minor"].Value),
            match.Groups["patch"].Success ? int.Parse(match.Groups["patch"].Value) : 0, stage);
    }
}

public sealed partial class JavaResourceSearchResultItem : ObservableObject
{
    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial string Summary { get; set; }
    [ObservableProperty] public partial string? IconUrl { get; set; }
    [ObservableProperty] public partial string Metadata { get; set; }
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconUrl);
    public IAsyncImageLoader ImageLoader { get; } = new ModImageLoader();
    public JavaResourceDetailsTarget Target { get; private set; }

    public JavaResourceSearchResultItem(ModrinthResource item, JavaResourceDefinition definition,
        string gameVersion, ModLoaderType loader)
    {
        Name = item.Name;
        Summary = item.Summary;
        IconUrl = item.IconUrl;
        Metadata = $"{FormatRelativeTime(item.Updated)}·{item.DownloadCount:N0} 下载";
        Target = new JavaResourceDetailsTarget(definition, ModDetailsSource.Modrinth, item.ProjectId,
            gameVersion, loader);
    }

    public JavaResourceSearchResultItem(CurseforgeResource item, JavaResourceDefinition definition,
        string gameVersion, ModLoaderType loader)
    {
        Name = item.Name;
        Summary = item.Summary;
        IconUrl = item.IconUrl;
        Metadata = $"{FormatRelativeTime(item.DateModified)}·{item.DownloadCount:N0} 下载";
        Target = new JavaResourceDetailsTarget(definition, ModDetailsSource.CurseForge, item.Id.ToString(),
            gameVersion, loader);
    }

    public void Update(JavaResourceSearchResultItem item)
    {
        Name = item.Name;
        Summary = item.Summary;
        IconUrl = item.IconUrl;
        Metadata = item.Metadata;
        Target = item.Target;
        OnPropertyChanged(nameof(HasIcon));
    }

    internal static string FormatRelativeTime(DateTime timestamp)
    {
        var localTime = timestamp.Kind == DateTimeKind.Utc ? timestamp.ToLocalTime() : timestamp;
        var elapsed = DateTime.Now - localTime;
        if (elapsed < TimeSpan.FromMinutes(1)) return "刚刚";
        if (elapsed < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)elapsed.TotalMinutes)} 分钟前";
        if (elapsed < TimeSpan.FromDays(1)) return $"{Math.Max(1, (int)elapsed.TotalHours)} 小时前";
        if (elapsed < TimeSpan.FromDays(7)) return $"{Math.Max(1, (int)elapsed.TotalDays)} 天前";
        if (elapsed < TimeSpan.FromDays(30)) return $"{Math.Max(1, (int)(elapsed.TotalDays / 7))} 周前";
        if (elapsed < TimeSpan.FromDays(365)) return $"{Math.Max(1, (int)(elapsed.TotalDays / 30))} 个月前";
        return $"{Math.Max(1, (int)(elapsed.TotalDays / 365))} 年前";
    }
}

public sealed record JavaResourceSearchSource(string DisplayName, SearchSource Kind);
public sealed record JavaResourceSearchRequest(JavaResourceKind Kind, SearchSource Source, string Query,
    string GameVersion, ModLoaderType Loader, SearchSort Sort, int Page);
public sealed record JavaResourceSearchPage(IReadOnlyList<JavaResourceSearchResultItem> Items, int TotalCount);

public sealed class ModpackSearchPageViewModel() : JavaResourceSearchViewModel(JavaResourceDefinitions.Modpack);
public sealed class ResourcePackSearchPageViewModel() : JavaResourceSearchViewModel(JavaResourceDefinitions.ResourcePack);
public sealed class ShaderPackSearchPageViewModel() : JavaResourceSearchViewModel(JavaResourceDefinitions.ShaderPack);
public sealed class DataPackSearchPageViewModel() : JavaResourceSearchViewModel(JavaResourceDefinitions.DataPack);
public sealed class SaveSearchPageViewModel() : JavaResourceSearchViewModel(JavaResourceDefinitions.Save);
public sealed class BedrockResourceSearchViewModel(JavaResourceDefinition definition) : JavaResourceSearchViewModel(definition);

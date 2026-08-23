using System.Collections.ObjectModel;
using AsyncImageLoader;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iridium.Enums.Resources;
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

public sealed record JavaResourceDefinition(
    JavaResourceKind Kind,
    string DisplayName,
    string ProjectType,
    int? CurseForgeClassId,
    bool SupportsDownload,
    bool SupportsLoaderFilter,
    bool SupportsModrinth = true,
    int CurseForgeGameId = 432,
    bool ShowIconPlaceholder = false);

public static class JavaResourceDefinitions
{
    public static JavaResourceDefinition Modpack { get; } =
        new(JavaResourceKind.Modpack, CommonLanguageManager.Instance.javaResourceSearch_modpack.CurrentValue(),
            "modpack", 4471, false, true, ShowIconPlaceholder: true);

    public static JavaResourceDefinition ResourcePack { get; } =
        new(JavaResourceKind.ResourcePack, CommonLanguageManager.Instance.javaResourceSearch_resourcePack.CurrentValue(),
            "resourcepack", 12, true, false);

    public static JavaResourceDefinition ShaderPack { get; } =
        new(JavaResourceKind.ShaderPack, CommonLanguageManager.Instance.javaResourceSearch_shaderPack.CurrentValue(),
            "shader", 6552, true, false);

    public static JavaResourceDefinition DataPack { get; } =
        new(JavaResourceKind.DataPack, CommonLanguageManager.Instance.javaResourceSearch_dataPack.CurrentValue(),
            "datapack", 6945, true, false);

    public static JavaResourceDefinition Save { get; } =
        new(JavaResourceKind.Save, CommonLanguageManager.Instance.javaResourceSearch_save.CurrentValue(), "world", 17,
            true, false, false);
}

public abstract partial class JavaResourceSearchViewModel : ObservableObject, IDisposable, ISearchPageViewModel
{
    private const int PageSize = 40;
    private static readonly BoundedCache<JavaResourceSearchRequest, JavaResourceSearchPage> Cache = new(32);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private bool _disposed;
    private CancellationTokenSource? _gameVersionDebounce;
    private bool _initialized;
    private bool _suppressFilterSearch;

    protected JavaResourceSearchViewModel(JavaResourceDefinition definition)
    {
        Definition = definition;
        Sources = definition.SupportsModrinth
            ?
            [
                new JavaResourceSearchSource("CurseForge", SearchSource.CurseForge),
                new JavaResourceSearchSource("Modrinth", SearchSource.Modrinth)
            ]
            : [new JavaResourceSearchSource("CurseForge", SearchSource.CurseForge)];
        SelectedSource = Sources.FirstOrDefault(source =>
                               source.Kind == DownloadSearchPersistence.ToUiSource(Data.ConfigEntry.DefaultDownloadSearchSource))
                           ?? Sources.Last();
        SelectedLoader = Loaders.FirstOrDefault(loader => loader.Kind == Data.ConfigEntry.DownloadSearchLoader)
                         ?? Loaders[0];
        SelectedSort = SortOptions.FirstOrDefault(sort =>
                           sort.Kind == DownloadSearchPersistence.ToUiSort(Data.ConfigEntry.DefaultDownloadSearchSort))
                       ?? SortOptions[0];
    }

    public JavaResourceDefinition Definition { get; }
    public string PageTitle => string.Format(CommonLanguageManager.Instance.startPage_searchModeTitle.CurrentValue(),
        Definition.DisplayName);
    public string SearchPlaceholder => string.Format(
        CommonLanguageManager.Instance.startPage_searchPlaceholderMode.CurrentValue(), Definition.DisplayName);
    public bool ShowLoaderFilter => Definition.SupportsLoaderFilter;
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

    [ObservableProperty] public partial JavaResourceSearchSource? SelectedSource { get; set; }
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

    partial void OnSelectedSourceChanged(JavaResourceSearchSource? value)
    {
        if (_initialized && value is not null)
            Data.ConfigEntry.DefaultDownloadSearchSource = DownloadSearchPersistence.ToCoreSource(value.Kind);

        _suppressFilterSearch = true;
        try
        {
            SelectedSort = SortOptions[0];
            GameVersion = string.Empty;
            SelectedLoader = Loaders[0];
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
        var request = new JavaResourceSearchRequest(Definition.Kind, SelectedSource.Kind, SearchText.Trim(),
            GameVersion.Trim(), ShowLoaderFilter ? SelectedLoader?.Kind ?? ModLoaderType.Any : ModLoaderType.Any,
            SelectedSort.Kind, CurrentPage);
        var renderedCache = Cache.TryGetValue(request, out var cached) && cached is not null && IsCurrent(request);
        if (renderedCache) Apply(cached!);

        if (IsCurrent(request))
        {
            HasError = false;
            StatusText = isDefaultSearch
                ? string.Format(CommonLanguageManager.Instance.javaResourceSearch_fetchingPopular.CurrentValue(),
                    Definition.DisplayName)
                : CommonLanguageManager.Instance.modSearch_searching.CurrentValue();
        }

        try
        {
            var page = await FetchAsync(request, _disposeCancellation.Token);
            Cache.Set(request, page);
            if (IsCurrent(request)) Apply(page, renderedCache);
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
            Logger.Debug($"[Download] {Definition.DisplayName} search cancelled.");
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            if (!IsCurrent(request)) return;
            HasError = true;
            StatusText = CommonLanguageManager.Instance.modSearch_networkError.CurrentValue();
        }
    }

    private async Task<JavaResourceSearchPage> FetchAsync(JavaResourceSearchRequest request,
        CancellationToken cancellationToken)
    {
        var options = new ResourceSearchOptions
        {
            Source = request.Source == SearchSource.Modrinth ? ResourceSource.Modrinth : ResourceSource.CurseForge,
            Type = IridiumResourceMapping.ParseResourceType(Definition.ProjectType),
            Query = string.IsNullOrWhiteSpace(request.Query) ? null : request.Query,
            GameVersion = string.IsNullOrWhiteSpace(request.GameVersion) ? null : request.GameVersion,
            Loader = request.Loader.ToResourceLoader(),
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
        StatusText = page.TotalCount == 0
            ? string.Format(CommonLanguageManager.Instance.javaResourceSearch_noResults.CurrentValue(),
                Definition.DisplayName)
            : string.Format(CommonLanguageManager.Instance.javaResourceSearch_resultCount.CurrentValue(),
                page.TotalCount, Definition.DisplayName);
        OnPropertyChanged(nameof(HasResults));
    }

    private bool IsCurrent(JavaResourceSearchRequest request)
    {
        return !_disposed && Definition.Kind == request.Kind &&
               SelectedSource?.Kind == request.Source && SearchText.Trim() == request.Query &&
               GameVersion.Trim() == request.GameVersion &&
               (ShowLoaderFilter ? SelectedLoader?.Kind ?? ModLoaderType.Any : ModLoaderType.Any) == request.Loader &&
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
    public JavaResourceSearchResultItem(ResourceHit hit, JavaResourceDefinition definition,
        string gameVersion, ModLoaderType loader)
    {
        Name = hit.Title ?? string.Empty;
        Summary = hit.Translation ?? hit.Summary ?? string.Empty;
        IconUrl = hit.IconUrl;
        Metadata = string.Format(CommonLanguageManager.Instance.mod_downloadCount.CurrentValue(),
            RelativeTime.Format(hit.DateModified ?? hit.DateCreated ?? default), hit.Downloads);
        Target = new JavaResourceDetailsTarget(definition,
            ModSearchResultItem.ToModDetailsSource(hit.Source), hit.Id, gameVersion, loader);
        IsFavorite =
            FavoriteCollectionService.Instance.Contains(FavoriteResourceFactory.From(this, GetEdition(definition)));
    }

    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial string Summary { get; set; }
    [ObservableProperty] public partial string? IconUrl { get; set; }
    [ObservableProperty] public partial string Metadata { get; set; }
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconUrl);
    [ObservableProperty] public partial bool IsFavorite { get; set; }
    public IAsyncImageLoader ImageLoader { get; } = new ModImageLoader();
    public JavaResourceDetailsTarget Target { get; private set; }
    public bool ShowIconPlaceholder => Target.Definition.ShowIconPlaceholder && !HasIcon;

    public void Update(JavaResourceSearchResultItem item)
    {
        Name = item.Name;
        Summary = item.Summary;
        IconUrl = item.IconUrl;
        Metadata = item.Metadata;
        Target = item.Target;
        OnPropertyChanged(nameof(HasIcon));
        IsFavorite = item.IsFavorite;
    }

    private static FavoriteEdition GetEdition(JavaResourceDefinition definition)
    {
        return definition.Kind is
            JavaResourceKind.BedrockBehaviorPack or JavaResourceKind.BedrockResourcePack
            or JavaResourceKind.BedrockWorld or JavaResourceKind.BedrockWorldTemplate
            ? FavoriteEdition.Bedrock
            : FavoriteEdition.Java;
    }
}

public sealed record JavaResourceSearchSource(string DisplayName, SearchSource Kind);

public sealed record JavaResourceSearchRequest(
    JavaResourceKind Kind,
    SearchSource Source,
    string Query,
    string GameVersion,
    ModLoaderType Loader,
    SearchSort Sort,
    int Page);

public sealed record JavaResourceSearchPage(IReadOnlyList<JavaResourceSearchResultItem> Items, int TotalCount);

public sealed class ModpackSearchPageViewModel() : JavaResourceSearchViewModel(JavaResourceDefinitions.Modpack);

public sealed class ResourcePackSearchPageViewModel()
    : JavaResourceSearchViewModel(JavaResourceDefinitions.ResourcePack);

public sealed class ShaderPackSearchPageViewModel() : JavaResourceSearchViewModel(JavaResourceDefinitions.ShaderPack);

public sealed class DataPackSearchPageViewModel() : JavaResourceSearchViewModel(JavaResourceDefinitions.DataPack);

public sealed class SaveSearchPageViewModel() : JavaResourceSearchViewModel(JavaResourceDefinitions.Save);

public sealed class BedrockResourceSearchViewModel(JavaResourceDefinition definition)
    : JavaResourceSearchViewModel(definition);

internal sealed class BoundedCache<TKey, TValue>(int capacity) where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _entries = new();
    private readonly object _lock = new();
    private readonly LinkedList<(TKey Key, TValue Value)> _usage = new();

    public bool TryGetValue(TKey key, out TValue? value)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out var node))
            {
                value = default;
                return false;
            }

            _usage.Remove(node);
            _usage.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
    }

    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_entries.Remove(key, out var existing))
                _usage.Remove(existing);

            var node = _usage.AddFirst((key, value));
            _entries[key] = node;
            if (_entries.Count <= capacity) return;

            var oldest = _usage.Last!;
            _usage.RemoveLast();
            _entries.Remove(oldest.Value.Key);
        }
    }
}

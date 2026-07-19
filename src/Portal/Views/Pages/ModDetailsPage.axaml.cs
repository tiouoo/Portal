using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using AsyncImageLoader;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Provider;
using Portal.Views.Pages.InstancePages;
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
public sealed record ModDetailsTarget(ModDetailsSource Source, string ProjectId);

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

public partial class ModDetailsPageViewModel(ModDetailsTarget target) : ObservableObject
{
    private readonly ModrinthProvider _modrinth = new();
    private readonly CurseforgeProvider _curseforge = new();
    private bool _loaded;
    public ObservableCollection<ModMinecraftVersionFilter> VersionFilters { get; } = [];
    public ObservableCollection<ModLoaderFilter> LoaderFilters { get; } = [];
    public ObservableCollection<ModVersionGroup> VersionGroups { get; } = [];
    public ObservableCollection<string> Screenshots { get; } = [];
    public ObservableCollection<int> ScreenshotIndices { get; } = [];
    public IAsyncImageLoader ImageLoader { get; } = new ModImageLoader();
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
    private List<ModVersionFileItem> Files { get; } = [];

    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        IsLoading = true;
        try
        {
            if (target.Source == ModDetailsSource.Modrinth)
            {
                var project = await _modrinth.SearchByProjectIdAsync(target.ProjectId);
                Name = project.Name;
                FriendlyName = WikiEntries.FindChineseName(project.Slug) ?? project.Name;
                Summary = project.Summary;
                IconUrl = project.IconUrl;
                Metadata = FormatMetadata(project.Updated, project.DownloadCount, "Modrinth");
                AddScreenshots(project.Screenshots);
                Files.AddRange(
                    (await _modrinth.GetModFilesByProjectIdAsync(target.ProjectId)).Select(ModVersionFileItem.From));
            }
            else
            {
                var project = (await _curseforge.GetResourcesByModIdsAsync([long.Parse(target.ProjectId)])).First();
                Name = project.Name;
                FriendlyName = WikiEntries.FindChineseName(project.Slug) ?? project.Name;
                Summary = project.Summary;
                IconUrl = project.IconUrl;
                Metadata = FormatMetadata(project.DateModified, project.DownloadCount, "CurseForge");
                AddScreenshots(project.Screenshots);
                Files.AddRange((await _curseforge.GetModFilesAsync(project.Id)).Select(ModVersionFileItem.From));
            }

            BuildFilters();
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

    partial void OnSelectedVersionFilterChanged(ModMinecraftVersionFilter? value) => ApplyFilter();
    partial void OnSelectedLoaderFilterChanged(ModLoaderFilter? value) => ApplyFilter();

    private void BuildFilters()
    {
        var families = Files.SelectMany(file => file.MinecraftVersions).Select(GetVersionFamily)
            .Where(family => family != null).Distinct().OrderByDescending(family => MinecraftVersionKey.Parse(family!));
        VersionFilters.Clear();
        VersionFilters.Add(new ModMinecraftVersionFilter("全部", null));
        foreach (var family in families) VersionFilters.Add(new ModMinecraftVersionFilter(family!, family));
        var loaders = Files.SelectMany(file => file.GroupKeys).Select(key => key.Loader).Distinct().Order();
        LoaderFilters.Clear();
        LoaderFilters.Add(new ModLoaderFilter("全部", null));
        foreach (var loader in loaders) LoaderFilters.Add(new ModLoaderFilter(loader, loader));
        SelectedVersionFilter = VersionFilters[0];
        SelectedLoaderFilter = LoaderFilters[0];
        OnPropertyChanged(nameof(HasVersions));
    }

    private void ApplyFilter()
    {
        var selectedFamily = SelectedVersionFilter?.Family;
        var selectedLoader = SelectedLoaderFilter?.Loader;
        var groups = Files.Where(file =>
                selectedFamily == null ||
                file.MinecraftVersions.Any(version => GetVersionFamily(version) == selectedFamily))
            .SelectMany(file => file.GroupKeys.Select(key => (Key: key, File: file)))
            .Where(item => (selectedFamily == null || GetVersionFamily(item.Key.MinecraftVersion) == selectedFamily) &&
                           (selectedLoader == null || item.Key.Loader == selectedLoader))
            .GroupBy(item => item.Key)
            .OrderByDescending(group => MinecraftVersionKey.Parse(group.Key.MinecraftVersion))
            .ThenBy(group => group.Key.Loader)
            .Select(group => new ModVersionGroup($"{group.Key.Loader} {group.Key.MinecraftVersion}",
                group.Select(item => item.File).DistinctBy(file => file.Id).ToList()));
        VersionGroups.Clear();
        foreach (var group in groups) VersionGroups.Add(group);
        OnPropertyChanged(nameof(IsEmpty));
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
        $"{source}·更新于 {updated.ToLocalTime():yyyy-MM-dd}·{downloadCount:N0} 下载";

    private static string? GetVersionFamily(string version)
    {
        var match = Regex.Match(version, @"^(\d+)\.(\d+)(?:\.\d+)?$");
        return match.Success ? $"{match.Groups[1].Value}.{match.Groups[2].Value}" : null;
    }
}

public sealed record ModMinecraftVersionFilter(string DisplayName, string? Family);

public sealed record ModLoaderFilter(string DisplayName, string? Loader);

public sealed record ModVersionGroup(string Title, IReadOnlyList<ModVersionFileItem> Files)
{
    public string FileCountText => $"{Files.Count} 个文件";
}

public sealed record ModVersionGroupKey(string Loader, string MinecraftVersion);

public sealed record ModVersionFileItem(
    string Id,
    string DisplayName,
    string Details,
    string ReleaseTypeText,
    IReadOnlyList<string> MinecraftVersions,
    IReadOnlyList<ModVersionGroupKey> GroupKeys)
{
    public static ModVersionFileItem From(ModrinthResourceFile file) => new(file.VersionId,
        string.IsNullOrWhiteSpace(file.DisplayName) ? file.FileName : file.DisplayName,
        $"{file.VersionNumber} · {file.FileName} · {file.Published.ToLocalTime():yyyy-MM-dd}",
        ReleaseType(file.ReleaseType),
        file.MinecraftVersions.ToList(),
        file.ModLoaders.SelectMany(loader =>
            file.MinecraftVersions.Select(version => new ModVersionGroupKey(LoaderName(loader), version))).ToList());

    public static ModVersionFileItem From(CurseforgeResourceFile file)
    {
        var versions = file.GameVersions.Where(version => Regex.IsMatch(version, @"^\d+\.\d+(?:\.\d+)?$")).ToList();
        var loaders = file.GameVersions.Where(version => !Regex.IsMatch(version, @"^\d+\.\d+(?:\.\d+)?$"))
            .Select(LoaderName).DefaultIfEmpty("通用");
        return new(file.Id.ToString(), string.IsNullOrWhiteSpace(file.DisplayName) ? file.FileName : file.DisplayName,
            $"{file.FileName} · {file.Published.ToLocalTime():yyyy-MM-dd}", ReleaseType(file.ReleaseType), versions,
            loaders.SelectMany(loader => versions.Select(version => new ModVersionGroupKey(loader, version))).ToList());
    }

    private static string LoaderName(ModLoaderType loader) => loader switch
    {
        ModLoaderType.NeoForge => "NeoForge", ModLoaderType.Forge => "Forge", ModLoaderType.Fabric => "Fabric",
        ModLoaderType.Quilt => "Quilt", _ => "通用"
    };

    private static string LoaderName(string loader) => loader.ToLowerInvariant() switch
    {
        "neoforge" => "NeoForge", "forge" => "Forge", "fabric" => "Fabric", "quilt" => "Quilt", _ => loader
    };

    private static string ReleaseType(FileReleaseType type) => type switch
    {
        FileReleaseType.Release => "正式版", FileReleaseType.Beta => "Beta", _ => "Alpha"
    };
}

public readonly record struct MinecraftVersionKey(int Major, int Minor) : IComparable<MinecraftVersionKey>
{
    public static MinecraftVersionKey Parse(string version)
    {
        var pieces = version.Split('.');
        return new MinecraftVersionKey(int.Parse(pieces[0]), int.Parse(pieces[1]));
    }

    public int CompareTo(MinecraftVersionKey other) =>
        Major != other.Major ? Major.CompareTo(other.Major) : Minor.CompareTo(other.Minor);
}
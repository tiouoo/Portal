using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using AsyncImageLoader;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLaunch.Utilities;
using MinecraftLaunch.Components.Downloader;
using Portal.Core.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Models;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Portal.Module.Imaging;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.DownloadPages;

public partial class LeviLaminaSearchPage : UserControl
{
    public LeviLaminaSearchPage()
    {
        InitializeComponent();
        DataContext = new LeviLaminaSearchViewModel();
        Loaded += async (_, _) => await ((LeviLaminaSearchViewModel)DataContext).InitializeAsync();
    }

    private void SearchBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is LeviLaminaSearchViewModel vm)
        {
            vm.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void Card_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button) return;
        if (!e.GetCurrentPoint(sender as Control).Properties.IsLeftButtonPressed) return;
        if ((sender as Control)?.Tag is LeviLaminaSearchResultItem item && TopLevel.GetTopLevel(this) is { } topLevel)
            await LeviLaminaDownloadService.ShowInstallAsync(topLevel, item);
    }

    private async void Download_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is LeviLaminaSearchResultItem item && TopLevel.GetTopLevel(this) is { } topLevel)
            await LeviLaminaDownloadService.ShowInstallAsync(topLevel, item);
    }

    private async void OpenProject_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is LeviLaminaSearchResultItem item)
            await TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(new Uri(item.ProjectUrl));
    }

    private void Favorite_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is LeviLaminaSearchResultItem item)
        {
            var resource = FavoriteResourceFactory.From(item);
            if (item.IsFavorite) FavoriteCollectionService.Instance.Remove(resource);
            else FavoriteCollectionService.Instance.Add(resource);
            item.IsFavorite = !item.IsFavorite;
        }

        e.Handled = true;
    }

    private async void SaveAs_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is LeviLaminaSearchResultItem item &&
            TopLevel.GetTopLevel(this) is { } topLevel)
            await LeviLaminaDownloadService.DownloadAsync(topLevel, item);
        e.Handled = true;
    }
}

public sealed partial class LeviLaminaSearchViewModel : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _disposeCancellation = new();
    private bool _initialized;
    private bool _disposed;
    private static LiprResponse? _cache;
    private static readonly SemaphoreSlim CacheLock = new(1, 1);

    public ObservableCollection<LeviLaminaSearchResultItem> Results { get; } = [];
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool HasError { get; set; }
    public bool IsEmpty => !IsLoading && Results.Count == 0;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await SearchAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (_disposed) return;
        IsLoading = true;
        HasError = false;
        OnPropertyChanged(nameof(IsEmpty));
        StatusText = CommonLanguageManager.Instance.modSearch_searching.CurrentValue();
        try
        {
            var data = await LoadAsync(_disposeCancellation.Token);
            var keyword = SearchText.Trim();
            var items = data.Packages
                .Where(pair => Matches(pair.Key, pair.Value, keyword))
                .OrderByDescending(pair => pair.Value.StargazerCount)
                .ThenByDescending(pair => pair.Value.UpdatedAt)
                .Select(pair => new LeviLaminaSearchResultItem(pair.Key, pair.Value))
                .ToArray();
            Results.Clear();
            foreach (var item in items) Results.Add(item);
            StatusText = items.Length == 0
                ? DownloadsLanguageManager.Instance.levilaminasearchpage_noResults.CurrentValue()
                : string.Format(DownloadsLanguageManager.Instance.levilaminasearchpage_resultCount.CurrentValue(),
                    items.Length);
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            Results.Clear();
            HasError = true;
            StatusText = DownloadsLanguageManager.Instance.levilaminasearchpage_networkError.CurrentValue();
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private static bool Matches(string key, LiprPackage package, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        var text =
            $"{key} {package.Info?.Name} {package.Info?.Description} {string.Join(' ', package.Info?.Tags ?? [])}";
        return text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<LiprResponse> LoadAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null) return _cache;
        await CacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache is not null) return _cache;
            var json = await HttpUtil.Client.GetStringAsync("https://lipr.levimc.org/levilauncher.json",
                cancellationToken);
            _cache = JsonSerializer.Deserialize<LiprResponse>(json) ??
                     throw new InvalidDataException("Invalid LIPR response.");
            _cache.Packages = _cache.Packages.Where(pair => pair.Value.Variants.ContainsKey("client"))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            return _cache;
        }
        finally
        {
            CacheLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _disposeCancellation.Cancel();
        _disposeCancellation.Dispose();
        Results.Clear();
    }
}

public sealed partial class LeviLaminaSearchResultItem : ObservableObject
{
    public LeviLaminaSearchResultItem(string key, LiprPackage package)
    {
        Key = key;
        Package = package;
        Name = package.Info?.Name ?? key.Split('/').Last();
        Summary = package.Info?.Description ?? string.Empty;
        Tags = package.Info?.Tags ?? [];
        AvatarUrl = package.Info?.AvatarUrl;
        DisplayAvatarUrl = IsRasterUrl(AvatarUrl) ? AvatarUrl : null;
        ProjectUrl = $"http://{key}";
        Metadata = $"{package.StargazerCount:N0}·{package.UpdatedAt:yyyy-MM-dd}";
        IEnumerable<string> versions = package.Variants.TryGetValue("client", out var client)
            ? client.Versions.Keys
            : Array.Empty<string>();
        LatestVersion = versions.OrderByDescending(VersionKey).FirstOrDefault() ?? string.Empty;
        IsFavorite = FavoriteCollectionService.Instance.Contains(FavoriteResourceFactory.From(this));
    }

    public string Key { get; }
    public LiprPackage Package { get; }
    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial string Summary { get; set; }
    [ObservableProperty] public partial bool IsFavorite { get; set; }
    public string Metadata { get; }
    public string LatestVersion { get; }
    public string? AvatarUrl { get; }
    public string? DisplayAvatarUrl { get; }
    public bool HasIcon => !string.IsNullOrWhiteSpace(DisplayAvatarUrl);
    public string ProjectUrl { get; }
    public IReadOnlyList<string> Tags { get; }
    public IAsyncImageLoader ImageLoader { get; } = new ModImageLoader();

    private static Version VersionKey(string value) =>
        Version.TryParse(value.Split('-', 2)[0], out var v) ? v : new Version(0, 0);

    private static bool IsRasterUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var path = uri.AbsolutePath;
        return path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class LiprResponse
{
    [JsonPropertyName("packages")] public Dictionary<string, LiprPackage> Packages { get; set; } = [];
}

public sealed class LiprPackage
{
    [JsonPropertyName("stargazer_count")] public int StargazerCount { get; set; }
    [JsonPropertyName("updated_at")] public DateTime UpdatedAt { get; set; }
    [JsonPropertyName("info")] public LiprInfo? Info { get; set; }
    [JsonPropertyName("variants")] public Dictionary<string, LiprVariant> Variants { get; set; } = [];
}

public sealed class LiprInfo
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = [];
    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; set; }
}

public sealed class LiprVariant
{
    [JsonPropertyName("versions")] public Dictionary<string, LiprVersion> Versions { get; set; } = [];
}

public sealed class LiprVersion
{
    [JsonPropertyName("dependencies")] public Dictionary<string, string> Dependencies { get; set; } = [];
}

internal static class LeviLaminaDownloadService
{
    public static async Task ShowInstallAsync(TopLevel topLevel, LeviLaminaSearchResultItem item)
    {
        var dialog = new LeviLaminaInstallDialogViewModel(item);
        var result = await OverlayDialog
            .ShowCustomAsync<LeviLaminaInstallDialog, LeviLaminaInstallDialogViewModel, LeviLaminaInstallResult>(
                dialog, topLevel.TryGetHostId(),
                new OverlayDialogOptions { Title = item.Name, Buttons = DialogButton.None, CanResize = false });
        if (result is not null) await InstallAsync(topLevel, item, result);
    }

    public static async Task DownloadAsync(TopLevel topLevel, LeviLaminaSearchResultItem item)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(item.LatestVersion)) return;
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = DownloadsLanguageManager.Instance.levilaminasearchpage_download.CurrentValue(),
                SuggestedFileName =
                    string.Format(
                        DownloadsLanguageManager.Instance.levilaminasearchpage_downloadFileName.CurrentValue(),
                        item.Name, item.LatestVersion),
                DefaultExtension = "zip", FileTypeChoices = [new FilePickerFileType("ZIP") { Patterns = ["*.zip"] }]
            });
            var path = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) return;
            var tooth = await LoadToothAsync(item.Key, item.LatestVersion, CancellationToken.None);
            var asset = SelectAsset(tooth, item.Key, item.LatestVersion);
            var url = ResolveUrl(asset.Url, item.Key, item.LatestVersion);
            var task = DownloadTasks.Download(topLevel,
                string.Format(DownloadsLanguageManager.Instance.levilaminasearchpage_downloadTaskName.CurrentValue(),
                    item.Name),
                item.Name, Path.GetFileName(path), url, path, 0);
            await task.Completion;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            topLevel.Notice(DownloadsLanguageManager.Instance.levilaminasearchpage_networkError.CurrentValue(),
                NotificationType.Error);
        }
    }

    public static async Task InstallFavoriteAsync(TopLevel topLevel, FavoriteResource resource)
    {
        if (resource.Source != ModDetailsSource.LeviLamina || string.IsNullOrWhiteSpace(resource.ProjectId)) return;
        var packages = await LoadLiprAsync(CancellationToken.None);
        if (!packages.TryGetValue(resource.ProjectId, out var package))
        {
            var match = packages.FirstOrDefault(x =>
                string.Equals(NormalizeKey(x.Key), NormalizeKey(resource.ProjectId), StringComparison.OrdinalIgnoreCase));
            package = match.Value;
        }

        if (package is null) return;
        await ShowInstallAsync(topLevel,
            new LeviLaminaSearchResultItem(resource.ProjectId, package));
    }

    private static async Task InstallAsync(TopLevel topLevel, LeviLaminaSearchResultItem item,
        LeviLaminaInstallResult result)
    {
        try
        {
            var toothKey = NormalizeKey(item.Key);
            var plan = await CreateInstallationPlanAsync(result.Instance, toothKey, result.Version,
                item.Name, result.Dependencies, CancellationToken.None);
            foreach (var package in plan)
                await InstallPackageAsync(topLevel, result.Instance, package);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            topLevel.Notice(DownloadsLanguageManager.Instance.levilaminasearchpage_networkError.CurrentValue(),
                NotificationType.Error);
        }
    }

    private static async Task<IReadOnlyList<LeviInstallPackage>> CreateInstallationPlanAsync(
        MinecraftInstance instance, string rootKey, string rootVersion, string rootDisplayName,
        IReadOnlyList<LeviDependency> dependencies, CancellationToken cancellationToken)
    {
        var plan = new List<LeviInstallPackage>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        async Task VisitAsync(string key, string version, string displayName)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add($"{key}@{version}")) return;

            var tooth = await LoadToothAsync(key, version, cancellationToken);
            foreach (var dependency in tooth.Dependencies)
            {
                var dependencyKey = NormalizeKey(dependency.Key);
                var resolved = dependencies.FirstOrDefault(item =>
                    string.Equals(NormalizeKey(item.Key), dependencyKey, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(item.Version) && Satisfies(item.Version, dependency.Value));
                if (resolved?.Version is not { } dependencyVersion)
                    throw new InvalidDataException(
                        $"Dependency snapshot is incomplete for {dependencyKey} ({dependency.Value}).");
                await VisitAsync(dependencyKey, dependencyVersion, dependencyKey.Split('/').Last());
            }

            if (LeviLaminaInstallState.IsDependencyInstalled(instance, key, version)) return;
            var asset = SelectAsset(tooth, key, version);
            plan.Add(new LeviInstallPackage(key, version, displayName, asset));
        }

        await VisitAsync(rootKey, rootVersion, rootDisplayName);
        foreach (var dependency in dependencies)
        {
            if (dependency.Version is null ||
                !visited.Contains($"{NormalizeKey(dependency.Key)}@{dependency.Version}"))
                throw new InvalidDataException("Dependency snapshot changed before installation.");
        }

        return plan;
    }

    private static async Task InstallPackageAsync(TopLevel topLevel, MinecraftInstance instance,
        LeviInstallPackage package)
    {
        var url = ResolveUrl(package.Asset.Url, package.Key, package.Version);
        var temp = Path.Combine(Path.GetTempPath(), "Portal", "LeviLamina", $"{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        try
        {
            var task = DownloadTasks.Download(topLevel,
                string.Format(DownloadsLanguageManager.Instance.levilaminasearchpage_downloadTaskName.CurrentValue(),
                    package.DisplayName),
                package.DisplayName, Path.GetFileName(temp), url, temp, 0,
                async context =>
                    await ExtractAsync(temp, instance.InstanceFolderPath, package.Asset.Placements,
                        context.CancellationToken));
            await task.Completion;
        }
        finally
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch
            {
            }
        }
    }

    private static async Task ExtractAsync(string zipPath, string targetRoot,
        IReadOnlyList<LeviPlacement> placements, CancellationToken cancellationToken)
    {
        var extractRoot = Path.Combine(Path.GetTempPath(), "Portal", "LeviLamina", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractRoot);
        try
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractRoot, true);
            foreach (var placement in placements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var src = placement.Src?.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
                var dest = placement.Dest?.Replace('/', Path.DirectorySeparatorChar)
                    .TrimEnd(Path.DirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dest)) continue;
                var wildcard = src.EndsWith("*", StringComparison.Ordinal);
                if (wildcard) src = src.TrimEnd('*').TrimEnd(Path.DirectorySeparatorChar);
                var source = Path.GetFullPath(Path.Combine(extractRoot, src));
                if (!File.Exists(source) && !Directory.Exists(source))
                {
                    var roots = Directory.EnumerateDirectories(extractRoot).ToArray();
                    if (roots.Length == 1)
                        source = Path.GetFullPath(Path.Combine(roots[0], src));
                }
                var destination = Path.GetFullPath(Path.Combine(targetRoot, dest));
                if (!source.StartsWith(Path.GetFullPath(extractRoot), StringComparison.OrdinalIgnoreCase) ||
                    !destination.StartsWith(Path.GetFullPath(targetRoot), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unsafe package placement.");
                if (Directory.Exists(source)) CopyDirectory(source, destination);
                else if (File.Exists(source))
                {
                    Directory.CreateDirectory(destination);
                    File.Copy(source, Path.Combine(destination, Path.GetFileName(source)), true);
                }
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(extractRoot)) Directory.Delete(extractRoot, true);
            }
            catch
            {
            }

            await Task.CompletedTask;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static async Task<LeviTooth> LoadToothAsync(string key, string version, CancellationToken cancellationToken)
    {
        string json;
        try
        {
            json = await HttpUtil.Client.GetStringAsync(
                $"https://fastly.jsdelivr.net/gh/{ToRepoPath(key)}@v{version}/tooth.json", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            json = await HttpUtil.Client.GetStringAsync(
                $"https://raw.githubusercontent.com/{ToRepoPath(key)}/v{version}/tooth.json", cancellationToken);
        }
        return JsonSerializer.Deserialize<LeviTooth>(json,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
               throw new InvalidDataException("Invalid tooth.json");
    }

    internal static async Task<IReadOnlyDictionary<string, LiprPackage>> LoadLiprAsync(
        CancellationToken cancellationToken)
    {
        var json = await HttpUtil.Client.GetStringAsync("https://lipr.levimc.org/levilauncher.json", cancellationToken);
        return (JsonSerializer.Deserialize<LiprResponse>(json) ??
                throw new InvalidDataException("Invalid LIPR response.")).Packages;
    }

    internal static async Task<IReadOnlyList<LeviDependency>> ExpandToothDependenciesAsync(string rootKey,
        string rootVersion, IReadOnlyDictionary<string, LiprPackage> packages, CancellationToken cancellationToken)
    {
        var result = new List<LeviDependency>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"{NormalizeKey(rootKey)}@{rootVersion}"
        };
        await ExpandToothDependenciesAsync(rootKey, rootVersion, 0, packages, visited, result, cancellationToken);
        return result;
    }

    private static async Task ExpandToothDependenciesAsync(string key, string version, int level,
        IReadOnlyDictionary<string, LiprPackage> packages, HashSet<string> visited,
        List<LeviDependency> result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tooth = await LoadToothAsync(key, version, cancellationToken);
        foreach (var dependency in tooth.Dependencies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dependencyKey = NormalizeKey(dependency.Key);
            var dependencyVersion = await ResolveDependencyVersionAsync(dependencyKey, dependency.Value,
                packages, cancellationToken);
            if (string.IsNullOrWhiteSpace(dependencyVersion))
                throw new InvalidDataException(
                    $"Unable to resolve LeviLamina dependency {dependencyKey} ({dependency.Value}).");
            if (!visited.Add($"{dependencyKey}@{dependencyVersion}")) continue;
            result.Add(new LeviDependency(dependencyKey, dependency.Value, dependencyVersion, level));
            await ExpandToothDependenciesAsync(dependencyKey, dependencyVersion, level + 1, packages, visited,
                result, cancellationToken);
        }
    }

    private static async Task<string?> ResolveDependencyVersionAsync(string key, string constraint,
        IReadOnlyDictionary<string, LiprPackage> packages, CancellationToken cancellationToken)
    {
        var package = packages.FirstOrDefault(item =>
            string.Equals(NormalizeKey(item.Key), key, StringComparison.OrdinalIgnoreCase)).Value;
        IEnumerable<string> versions = package?.Variants.TryGetValue("client", out var variant) == true
            ? variant.Versions.Keys
            : Array.Empty<string>();
        var resolved = versions.Where(version => Satisfies(version, constraint))
            .OrderByDescending(ParseVersion).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(resolved)) return resolved;

        var repository = ToRepoPath(key);
        var json = await HttpUtil.Client.GetStringAsync(
            $"https://api.github.com/repos/{repository}/tags?per_page=100", cancellationToken);
        var tags = JsonSerializer.Deserialize<List<GitHubTag>>(json) ?? [];
        return tags.Select(tag => tag.Name.TrimStart('v'))
            .Where(version => Satisfies(version, constraint))
            .OrderByDescending(ParseVersion).FirstOrDefault();
    }

    private static bool Satisfies(string version, string constraint)
    {
        foreach (var alternative in constraint.Split("||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var value = ParseVersion(version);
            if (value is null) continue;
            var matches = true;
            foreach (var token in alternative.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var op = token.StartsWith(">=") || token.StartsWith("<=") ? token[..2] :
                    token.StartsWith('>') || token.StartsWith('<') || token.StartsWith('=') ? token[..1] : "";
                var raw = token[op.Length..];
                if (raw.Contains('*') || raw.Contains('x', StringComparison.OrdinalIgnoreCase))
                {
                    var prefix = raw[..raw.IndexOfAny(['*', 'x', 'X'])];
                    if (!version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) matches = false;
                    continue;
                }

                if (string.IsNullOrEmpty(op) && raw.Contains('-'))
                {
                    if (!string.Equals(version.TrimStart('v'), raw.TrimStart('v'),
                            StringComparison.OrdinalIgnoreCase))
                        matches = false;
                    continue;
                }

                var bound = ParseVersion(raw);
                if (bound is null) { matches = false; continue; }
                var cmp = value.CompareTo(bound);
                if (op == ">=" && cmp < 0 || op == "<=" && cmp > 0 || op == ">" && cmp <= 0 ||
                    op == "<" && cmp >= 0 || op == "=" && cmp != 0 || string.IsNullOrEmpty(op) && cmp != 0)
                    matches = false;
            }

            if (matches) return true;
        }

        return false;
    }

    private static Version? ParseVersion(string value) =>
        Version.TryParse(value.Split('-', 2)[0].TrimStart('v'), out var v) ? v : null;

    internal static string NormalizeKey(string key) =>
        key.Split('#')[0].StartsWith("github.com/", StringComparison.OrdinalIgnoreCase)
            ? key.Split('#')[0]
            : $"github.com/{key.Split('#')[0]}";

    private static string ToRepoPath(string key) => NormalizeKey(key)["github.com/".Length..];

    private static LeviAsset SelectAsset(LeviTooth tooth, string key, string version)
    {
        var variants = tooth.Variants ?? [];
        var candidateVariants = variants.Where(v =>
                string.Equals(v.Label, "client", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(v.Platform) ||
                 v.Platform.Contains("win", StringComparison.OrdinalIgnoreCase)))
            .Concat(variants.Where(v => string.Equals(v.Label, "client", StringComparison.OrdinalIgnoreCase)))
            .Concat(variants)
            .Distinct();
        foreach (var variant in candidateVariants)
        {
            foreach (var asset in variant.Assets ?? [])
            {
                var url = asset.Urls?.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate)) ??
                          asset.Url;
                if (string.IsNullOrWhiteSpace(url) &&
                    string.Equals(asset.Type, "self", StringComparison.OrdinalIgnoreCase))
                    url = $"https://codeload.github.com/{ToRepoPath(key)}/zip/refs/tags/v{version}";
                if (!string.IsNullOrWhiteSpace(url))
                    return new LeviAsset(url, asset.Placements ?? []);
            }
        }

        if (!string.IsNullOrWhiteSpace(tooth.AssetUrl))
            return new LeviAsset(tooth.AssetUrl, tooth.Files?.Place ?? []);
        throw new InvalidDataException("No installable asset found");
    }

    private static string ResolveUrl(string url, string key, string version) => url.Replace("$(version)", version)
        .Replace("{{version}}", version).Replace("{{tooth}}", NormalizeKey(key));
}

internal sealed record LeviDependency(string Key, string Constraint, string? Version, int Level);

internal sealed class GitHubTag
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
}

internal sealed record LeviAsset(string Url, IReadOnlyList<LeviPlacement> Placements);
internal sealed record LeviInstallPackage(string Key, string Version, string DisplayName, LeviAsset Asset);

internal sealed class LeviTooth
{
    [JsonPropertyName("asset_url")] public string? AssetUrl { get; set; }
    [JsonPropertyName("prerequisites")] public Dictionary<string, string> Prerequisites { get; set; } = [];
    [JsonPropertyName("dependencies")] public Dictionary<string, string> LegacyDependencies { get; set; } = [];
    [JsonPropertyName("variants")] public List<LeviVariant>? Variants { get; set; }
    [JsonPropertyName("files")] public LeviFiles? Files { get; set; }

    [JsonIgnore]
    public IReadOnlyDictionary<string, string> Dependencies => Prerequisites.Count > 0 ? Prerequisites :
        LegacyDependencies.Count > 0 ? LegacyDependencies :
        Variants?.FirstOrDefault(v => string.Equals(v.Label, "client", StringComparison.OrdinalIgnoreCase))
            ?.Dependencies ?? new Dictionary<string, string>();
}

internal sealed class LeviVariant
{
    public string? Label { get; set; }
    public string? Platform { get; set; }
    public Dictionary<string, string> Dependencies { get; set; } = [];
    public List<LeviOldAsset>? Assets { get; set; }
}

internal sealed class LeviOldAsset
{
    public string? Type { get; set; }
    public List<string>? Urls { get; set; }
    public string? Url { get; set; }
    public List<LeviPlacement>? Placements { get; set; }
}

internal sealed class LeviFiles
{
    public List<LeviPlacement> Place { get; set; } = [];
}

internal sealed class LeviPlacement
{
    public string? Type { get; set; }
    public string? Src { get; set; }
    public string? Dest { get; set; }
}

internal static class LeviLaminaInstallState
{
    public static bool IsLoaderInstalled(MinecraftInstance instance)
    {
        var root = instance.InstanceFolderPath;
        return File.Exists(Path.Combine(root, "config", "BedrockBoot2", "levilamina", "preloader", "bin",
                   "PreLoader.dll")) ||
               File.Exists(Path.Combine(root, "plugins", "LeviLamina", "manifest.json"));
    }

    public static bool IsDependencyInstalled(MinecraftInstance instance, string key, string? _)
    {
        if (Normalize(key).Equals("github.com/LiteLDev/LeviLamina", StringComparison.OrdinalIgnoreCase))
            return IsLoaderInstalled(instance);
        var name = key.Split('#')[0].Split('/').Last();
        var roots = new[]
        {
            Path.Combine(instance.InstanceFolderPath, "plugins"),
            Path.Combine(instance.InstanceFolderPath, "config", "BedrockBoot2", "levilamina", "ll.mods")
        };
        return roots.Any(root =>
            Directory.Exists(root) && Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories)
                .Any(file => ManifestMatches(file, name)));
    }

    private static bool ManifestMatches(string file, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            return document.RootElement.TryGetProperty("name", out var value) &&
                   string.Equals(value.GetString(), name, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string Normalize(string key) =>
        key.Split('#')[0].StartsWith("github.com/", StringComparison.OrdinalIgnoreCase)
            ? key.Split('#')[0]
            : $"github.com/{key.Split('#')[0]}";
}

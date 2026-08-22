using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using AsyncImageLoader;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iridium.Extensions.Resources;
using MinecraftLaunch.Base.Enums;
using Portal.Core.App.Helpers;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Models;
using Portal.Core.Minecraft.Services;
using Portal.Module.Imaging;
using Portal.Localization;
using Portal.Views.Pages.InstancePages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

using Portal.Module;
namespace Portal.Views.Pages.DownloadPages;

public sealed record ModDetailsTarget(
    ModDetailsSource Source,
    string ProjectId,
    string GameVersion = "",
    ModLoaderType Loader = ModLoaderType.Any);

public partial class ModDetailsPage : ResourceDetailsPageBase
{
    public ModDetailsPage() : this(new ModDetailsTarget(ModDetailsSource.Modrinth, string.Empty))
    {
    }

    public ModDetailsPage(ModDetailsTarget target)
    {
        InitializeComponent();
        ViewModel = new ModDetailsPageViewModel(target);
        DataContext = ViewModel;
        ViewModel.TargetVersionGroupReady += OnTargetVersionGroupReady;
        PageInfo = new PageInfo
        {
            Title = CommonLanguageManager.Instance.modDetails_title.CurrentValue(),
            IconGlyph = "\ue631", IconFont = IconResources.FontFamilyName
        };
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    public ModDetailsPageViewModel ViewModel { get; }

    public override void OnClose()
    {
        ViewModel.TargetVersionGroupReady -= OnTargetVersionGroupReady;
        base.OnClose();
        ViewModel.Dispose();
    }

    private void OnTargetVersionGroupReady(ModVersionGroup group)
    {
        QueueScrollTo(group);
    }

    private async void VersionFile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ModVersionFileItem file } ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var result = await OverlayDialog
            .ShowCustomAsync<ModInstallDialog, ModInstallDialogViewModel, ModInstallDialogResult>(
                new ModInstallDialogViewModel(file, InstanceManager.Instance.Instances), topLevel.TryGetHostId(),
                new OverlayDialogOptions
                    { Title = CommonLanguageManager.Instance.modDetails_downloadMod.CurrentValue(),
                        Buttons = DialogButton.None, CanLightDismiss = false, CanResize = false });
        if (result is null)
            return;

        var destination = result.Destination == ModDownloadDestination.Install && result.Instance is not null
            ? Path.Combine(result.Instance.GetSpecialFolder(MinecraftSpecialFolder.ModsFolder), file.FileName)
            : await SelectSaveDestinationAsync(topLevel, file);
        if (string.IsNullOrWhiteSpace(destination))
            return;

        StartDownload(topLevel, file, destination);
        if (result.Destination == ModDownloadDestination.Install && result.Instance is not null)
        {
            var modsFolder = result.Instance.GetSpecialFolder(MinecraftSpecialFolder.ModsFolder);
            foreach (var dependency in await ModDependencyFilter.FilterInstalledAsync(result.Instance,
                         result.Dependencies))
                StartDownload(topLevel, dependency, Path.Combine(modsFolder, dependency.FileName));
        }
    }

    public static async Task QuickDownloadAsync(TopLevel topLevel, ModDetailsTarget target)
    {
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
            IReadOnlyList<ModVersionFileItem> files = target.Source switch
            {
                ModDetailsSource.Modrinth =>
                    (await IridiumResourceClients.Modrinth.GetFilesAsync(target.ProjectId))
                    .Select(file => ModVersionFileItem.From(file.ToResourceFile())).ToArray(),
                ModDetailsSource.CurseForge => (await IridiumResourceClients.CurseForge.GetFilesAsync(
                        long.Parse(target.ProjectId)))
                    .Select(file => ModVersionFileItem.From(file.ToResourceFile())).ToArray(),
                _ => []
            };
            if (files.Count == 0)
                throw new InvalidDataException(CommonLanguageManager.Instance.modDetails_noModFiles.CurrentValue());
            loading.Close();
            await loadingDialog;

            await ShowInstallDialogAsync(topLevel, files);
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

    private static async Task ShowInstallDialogAsync(TopLevel topLevel, IReadOnlyList<ModVersionFileItem> files)
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
            : await SelectSaveDestinationAsync(topLevel, file);
        if (string.IsNullOrWhiteSpace(destination)) return;

        StartDownload(topLevel, file, destination);
        if (result.Destination == ModDownloadDestination.Install && result.Instance is not null)
        {
            var modsFolder = result.Instance.GetSpecialFolder(MinecraftSpecialFolder.ModsFolder);
            foreach (var dependency in await ModDependencyFilter.FilterInstalledAsync(result.Instance,
                         result.Dependencies))
                StartDownload(topLevel, dependency, Path.Combine(modsFolder, dependency.FileName));
        }
    }

    private static async Task<string?> SelectSaveDestinationAsync(TopLevel topLevel, ModVersionFileItem file)
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

    private static void StartDownload(TopLevel topLevel, ModVersionFileItem file, string destination)
    {
        DownloadTasks.Download(topLevel,
            string.Format(CommonLanguageManager.Instance.modDetails_downloadModFormat.CurrentValue(), file.FileName),
            CommonLanguageManager.Instance.modDetails_cancelModDownload.CurrentValue(), file.FileName,
            file.DownloadUrl, destination, file.FileSize,
            failureMessage: CommonLanguageManager.Instance.modDetails_modDownloadFailed.CurrentValue());
    }

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

public partial class ModDetailsPageViewModel(ModDetailsTarget target) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _disposeCancellation = new();
    private IReadOnlyList<ModVersionGroup> _allVersionGroups = [];
    private bool _buildingFilters;
    private bool _disposed;
    private CancellationTokenSource? _filterCancellation;
    private CancellationTokenSource? _filterDebounce;
    private bool _hasLocatedTargetVersionGroup;
    private bool _loaded;
    private int _nextVersionGroupIndex;
    public ObservableCollection<ModMinecraftVersionFilter> VersionFilters { get; } = [];
    public ObservableCollection<ModLoaderFilter> LoaderFilters { get; } = [];
    [ObservableProperty] public partial ObservableCollection<ModVersionGroup> VersionGroups { get; set; } = [];
    public ObservableCollection<string> Screenshots { get; } = [];
    public ObservableCollection<int> ScreenshotIndices { get; } = [];
    public IAsyncImageLoader ImageLoader { get; } = new ModImageLoader();
    public IAsyncImageLoader ScreenshotLoader { get; } = new ModScreenshotLoader();
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
    public bool HasMoreVersionGroups => _nextVersionGroupIndex < _allVersionGroups.Count;
    public string LoadMoreVersionGroupsText =>
        string.Format(CommonLanguageManager.Instance.modDetails_loadMoreVersionGroups.CurrentValue(),
            _allVersionGroups.Count - _nextVersionGroupIndex);
    private IReadOnlyList<ModVersionFileItem> Files { get; set; } = [];

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _buildingFilters = true;
        _filterCancellation = null;
        _filterDebounce = null;
        CancellationTokens.CancelInBackground(_disposeCancellation);

        VersionGroups = [];
        TargetVersionGroupReady = null;
        VersionFilters.Clear();
        LoaderFilters.Clear();
        Screenshots.Clear();
        ScreenshotIndices.Clear();
        SelectedVersionFilter = null;
        SelectedLoaderFilter = null;
        Files = [];
        _allVersionGroups = [];
    }

    public event Action<ModVersionGroup>? TargetVersionGroupReady;

    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        IsLoading = true;
        try
        {
            var cancellationToken = _disposeCancellation.Token;
            if (target.Source == ModDetailsSource.Modrinth)
            {
                var project = await IridiumResourceClients.Modrinth.GetProjectAsync(target.ProjectId, cancellationToken)
                              ?? throw new InvalidDataException(
                                  CommonLanguageManager.Instance.modDetails_noModFiles.CurrentValue());
                var translations = await ProjectTranslationService.GetTranslationsAsync(
                    ProjectTranslationSource.Modrinth,
                    [project.Id ?? string.Empty], cancellationToken);
                Name = project.Title ?? string.Empty;
                FriendlyName = WikiEntries.FindChineseName(project.Slug ?? string.Empty) ?? project.Title ?? string.Empty;
                Summary = translations.GetValueOrDefault(project.Id ?? string.Empty) ?? project.Description ?? string.Empty;
                IconUrl = project.IconUrl;
                Metadata = FormatMetadata(project.Updated ?? default, (int)project.Downloads, "Modrinth");
                AddScreenshots(project.Gallery.Select(gallery => gallery.Url));
                Files = await Task.Run(async () => (await IridiumResourceClients.Modrinth.GetFilesAsync(
                    target.ProjectId, cancellationToken: cancellationToken))
                    .Select(file => ModVersionFileItem.From(file.ToResourceFile())).ToArray(), cancellationToken);
            }
            else
            {
                var project = await IridiumResourceClients.CurseForge.GetProjectAsync(long.Parse(target.ProjectId),
                    cancellationToken) ?? throw new InvalidDataException(
                    CommonLanguageManager.Instance.modDetails_noModFiles.CurrentValue());
                var projectId = project.Id.ToString();
                var translations = await ProjectTranslationService.GetTranslationsAsync(
                    ProjectTranslationSource.CurseForge,
                    [projectId], cancellationToken);
                Name = project.Name ?? string.Empty;
                FriendlyName = WikiEntries.FindChineseName(project.Slug ?? string.Empty) ?? project.Name ?? string.Empty;
                Summary = translations.GetValueOrDefault(projectId) ?? project.Summary ?? string.Empty;
                IconUrl = project.Logo?.ThumbnailUrl ?? project.Logo?.Url;
                Metadata = FormatMetadata(project.DateModified ?? default,
                    (int)(project.DownloadCount ?? 0), "CurseForge");
                AddScreenshots(project.Screenshots.Select(screenshot => screenshot.Url ?? screenshot.ThumbnailUrl));
                Files = await Task.Run(async () => (await IridiumResourceClients.CurseForge.GetFilesAsync(project.Id,
                        cancellationToken: cancellationToken))
                    .Select(file => ModVersionFileItem.From(file.ToResourceFile())).ToArray(), cancellationToken);
            }

            await BuildFiltersAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
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

    partial void OnSelectedVersionFilterChanged(ModMinecraftVersionFilter? value)
    {
        if (!_disposed && !_buildingFilters) DebounceFilter();
    }

    partial void OnSelectedLoaderFilterChanged(ModLoaderFilter? value)
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
            var families = Files.SelectMany(file => file.MinecraftVersions).Select(GetVersionFamily)
                .Where(family => family != null).Distinct()
                .OrderByDescending(family => MinecraftVersionKey.Parse(family!))
                .Select(family => family!).ToArray();
            var loaders = Files.SelectMany(file => file.GroupKeys).Select(key => key.Loader).Distinct().Order()
                .ToArray();
            return (Families: families, Loaders: loaders);
        }, cancellationToken);
        if (cancellationToken.IsCancellationRequested) return;

        _buildingFilters = true;
        VersionFilters.Clear();
        VersionFilters.Add(new ModMinecraftVersionFilter(CommonLanguageManager.Instance.mod_all.CurrentValue(), null));
        foreach (var family in filterData.Families) VersionFilters.Add(new ModMinecraftVersionFilter(family, family));
        LoaderFilters.Clear();
        LoaderFilters.Add(new ModLoaderFilter(CommonLanguageManager.Instance.mod_all.CurrentValue(), null));
        foreach (var loader in filterData.Loaders) LoaderFilters.Add(new ModLoaderFilter(loader, loader));
        SelectedVersionFilter =
            VersionFilters.FirstOrDefault(filter => filter.Family == GetVersionFamily(target.GameVersion)) ??
            VersionFilters[0];
        SelectedLoaderFilter = LoaderFilters.FirstOrDefault(filter => filter.Loader == LoaderName(target.Loader)) ??
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
            var groups = await Task.Run(() => Files.Where(file =>
                    selectedFamily == null ||
                    file.MinecraftVersions.Any(version => GetVersionFamily(version) == selectedFamily))
                .SelectMany(file => file.GroupKeys.Select(key => (Key: key, File: file)))
                .Where(item =>
                    (selectedFamily == null || GetVersionFamily(item.Key.MinecraftVersion) == selectedFamily) &&
                    (selectedLoader == null || item.Key.Loader == selectedLoader))
                .GroupBy(item => item.Key)
                .OrderByDescending(group => MinecraftVersionKey.Parse(group.Key.MinecraftVersion))
                .ThenBy(group => group.Key.Loader)
                .Select(group => new ModVersionGroup($"{group.Key.Loader} {group.Key.MinecraftVersion}",
                    group.Select(item => item.File.ForCompatibility(item.Key)).DistinctBy(file => file.Id).ToArray(),
                    group.Key.Loader, group.Key.MinecraftVersion))
                .ToArray(), cancellation.Token);
            if (cancellation.IsCancellationRequested || _disposed) return;

            _allVersionGroups = groups;
            _nextVersionGroupIndex = 0;
            VersionGroups = [];
            LoadMoreVersionGroups();
            if (!_hasLocatedTargetVersionGroup && !string.IsNullOrWhiteSpace(target.GameVersion) &&
                VersionGroups.FirstOrDefault(group => group.MinecraftVersion == target.GameVersion &&
                                                      (LoaderName(target.Loader) is not { } targetLoader ||
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

    private void AddScreenshots(IEnumerable<string?>? urls)
    {
        if (urls == null) return;
        foreach (var url in urls.Where(url => !string.IsNullOrWhiteSpace(url)).Distinct())
        {
            Screenshots.Add(url!);
            ScreenshotIndices.Add(ScreenshotIndices.Count);
        }

        OnPropertyChanged(nameof(HasScreenshots));
    }

    private static string FormatMetadata(DateTime updated, int downloadCount, string source)
    {
        return string.Format(CommonLanguageManager.Instance.modDetails_metadataWithSource.CurrentValue(), source,
            RelativeTime.Format(updated), downloadCount);
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

public sealed record ModMinecraftVersionFilter(string DisplayName, string? Family);

public sealed record ModLoaderFilter(string DisplayName, string? Loader);

public sealed partial class ModVersionGroup : ObservableObject
{
    private const int PageSize = 20;
    private readonly IReadOnlyList<ModVersionFileItem> _files;

    public ModVersionGroup(string title, IReadOnlyList<ModVersionFileItem> files, string loader,
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
    public ObservableCollection<ModVersionFileItem> VisibleFiles { get; } = [];
    public string FileCountText => string.Format(CommonLanguageManager.Instance.mod_fileCount.CurrentValue(), _files.Count);
    public bool HasMore => VisibleFiles.Count < _files.Count;
    public string LoadMoreText =>
        string.Format(CommonLanguageManager.Instance.mod_loadMore.CurrentValue(),
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
using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text.RegularExpressions;
using AsyncImageLoader;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iridium.Enums.Resources;
using Iridium.Extensions.Resources;
using Iridium.Models.Resources;
using MinecraftLaunch.Base.Enums;
using Portal.Core.App.Helpers;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
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

public sealed record JavaResourceDetailsTarget(
    JavaResourceDefinition Definition,
    ModDetailsSource Source,
    string ProjectId,
    string GameVersion = "",
    ModLoaderType Loader = ModLoaderType.Any);

public abstract partial class JavaResourceDetailsViewModel(JavaResourceDetailsTarget target)
    : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _disposeCancellation = new();
    private IReadOnlyList<JavaResourceVersionGroup> _allVersionGroups = [];
    private bool _buildingFilters;
    private bool _disposed;
    private CancellationTokenSource? _filterCancellation;
    private bool _hasLocatedTargetVersionGroup;
    private bool _loaded;
    private int _nextVersionGroupIndex;
    public JavaResourceDetailsTarget Target { get; } = target;
    public ObservableCollection<JavaResourceVersionFilter> VersionFilters { get; } = [];
    public ObservableCollection<string> Screenshots { get; } = [];
    public ObservableCollection<int> ScreenshotIndices { get; } = [];
    [ObservableProperty] public partial ObservableCollection<JavaResourceVersionGroup> VersionGroups { get; set; } = [];
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string Summary { get; set; } = string.Empty;
    [ObservableProperty] public partial string Metadata { get; set; } = string.Empty;
    [ObservableProperty] public partial string? IconUrl { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool HasError { get; set; }
    [ObservableProperty] public partial JavaResourceVersionFilter? SelectedVersionFilter { get; set; }
    [ObservableProperty] public partial int SelectedScreenshotIndex { get; set; }
    public IAsyncImageLoader ImageLoader { get; } = new ModImageLoader();
    public IAsyncImageLoader ScreenshotLoader { get; } = new ModScreenshotLoader();
    public string LoadingText => string.Format(
        CommonLanguageManager.Instance.javaResourceDetails_loading.CurrentValue(),
        Target.Definition.DisplayName);
    public string ErrorText => string.Format(
        CommonLanguageManager.Instance.javaResourceDetails_error.CurrentValue(), Target.Definition.DisplayName);
    public bool HasScreenshots => Screenshots.Count > 0;
    public bool HasVersions => VersionFilters.Count > 0;
    public bool IsEmpty => !IsLoading && !HasError && VersionGroups.Count == 0;
    public bool SupportsDownload => Target.Definition.SupportsDownload;
    public bool HasMoreVersionGroups => _nextVersionGroupIndex < _allVersionGroups.Count;
    public string LoadMoreVersionGroupsText => string.Format(
        CommonLanguageManager.Instance.modDetails_loadMoreVersionGroups.CurrentValue(),
        _allVersionGroups.Count - _nextVersionGroupIndex);
    private IReadOnlyList<JavaResourceFileItem> AllFiles { get; set; } = [];

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _buildingFilters = true;
        _filterCancellation = null;
        CancellationTokens.CancelInBackground(_disposeCancellation);
        TargetVersionGroupReady = null;
        VersionGroups = [];
        VersionFilters.Clear();
        Screenshots.Clear();
        ScreenshotIndices.Clear();
        SelectedVersionFilter = null;
        AllFiles = [];
        _allVersionGroups = [];
    }

    public event Action<JavaResourceVersionGroup>? TargetVersionGroupReady;

    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        IsLoading = true;
        try
        {
            var cancellationToken = _disposeCancellation.Token;
            if (Target.Source == ModDetailsSource.Modrinth)
            {
                var project = await IridiumResourceClients.Modrinth.GetProjectAsync(Target.ProjectId, cancellationToken)
                              ?? throw new InvalidDataException(
                                  CommonLanguageManager.Instance.quickDownload_noFiles.CurrentValue());
                var translations = await ProjectTranslationService.GetTranslationsAsync(
                    ProjectTranslationSource.Modrinth,
                    [project.Id ?? string.Empty], cancellationToken);
                Name = project.Title ?? string.Empty;
                Summary = translations.GetValueOrDefault(project.Id ?? string.Empty) ?? project.Description ?? string.Empty;
                IconUrl = project.IconUrl;
                Metadata =
                    string.Format(CommonLanguageManager.Instance.mod_downloadCount.CurrentValue(),
                        RelativeTime.Format(project.Updated ?? default), project.Downloads);
                AddScreenshots(project.Gallery.Select(gallery => gallery.Url));
                AllFiles = await Task.Run(async () => (await IridiumResourceClients.Modrinth.GetFilesAsync(
                        Target.ProjectId, cancellationToken: cancellationToken))
                    .Select(file => JavaResourceFileItem.From(file.ToResourceFile())).ToArray(), cancellationToken);
            }
            else
            {
                var project = await IridiumResourceClients.CurseForge.GetProjectAsync(long.Parse(Target.ProjectId),
                    cancellationToken) ?? throw new InvalidDataException(
                    CommonLanguageManager.Instance.quickDownload_noFiles.CurrentValue());
                var projectId = project.Id.ToString();
                var translations = await ProjectTranslationService.GetTranslationsAsync(
                    ProjectTranslationSource.CurseForge,
                    [projectId], cancellationToken);
                Name = project.Name ?? string.Empty;
                Summary = translations.GetValueOrDefault(projectId) ?? project.Summary ?? string.Empty;
                IconUrl = project.Logo?.ThumbnailUrl ?? project.Logo?.Url;
                Metadata =
                    string.Format(CommonLanguageManager.Instance.mod_downloadCount.CurrentValue(),
                        RelativeTime.Format(project.DateModified ?? default), project.DownloadCount ?? 0);
                AddScreenshots(project.Screenshots.Select(screenshot => screenshot.Url ?? screenshot.ThumbnailUrl));
                AllFiles = await Task.Run(async () => (await IridiumResourceClients.CurseForge.GetFilesAsync(
                        project.Id, cancellationToken: cancellationToken))
                    .Select(file => JavaResourceFileItem.From(file.ToResourceFile())).ToArray(), cancellationToken);
            }

            await BuildVersionGroupsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
            Logger.Debug($"[Download] Details loading cancelled for {Target.ProjectId}.");
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            HasError = true;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private async Task BuildVersionGroupsAsync(CancellationToken cancellationToken)
    {
        var families = await Task.Run(() => AllFiles.SelectMany(file => file.MinecraftVersions).Select(GetVersionFamily)
            .Where(family => family is not null).Distinct()
            .OrderByDescending(family => MinecraftVersionKey.Parse(family!))
            .Select(family => family!).ToArray(), cancellationToken);
        if (_disposed || cancellationToken.IsCancellationRequested) return;
        _buildingFilters = true;
        VersionFilters.Clear();
        VersionFilters.Add(new JavaResourceVersionFilter(
            CommonLanguageManager.Instance.mod_all.CurrentValue(), null));
        foreach (var family in families) VersionFilters.Add(new JavaResourceVersionFilter(family, family));
        SelectedVersionFilter =
            VersionFilters.FirstOrDefault(filter => filter.Family == GetVersionFamily(Target.GameVersion)) ??
            VersionFilters[0];
        _buildingFilters = false;
        await ApplyVersionFilterAsync();
        OnPropertyChanged(nameof(HasVersions));
    }

    partial void OnSelectedVersionFilterChanged(JavaResourceVersionFilter? value)
    {
        if (!_disposed && !_buildingFilters) _ = ApplyVersionFilterAsync();
    }

    private async Task ApplyVersionFilterAsync()
    {
        if (_disposed) return;
        var selectedFamily = SelectedVersionFilter?.Family;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellation.Token);
        var previous = Interlocked.Exchange(ref _filterCancellation, cancellation);
        previous?.Cancel();
        try
        {
            var groups = await Task.Run(() => AllFiles.Where(file => selectedFamily is null ||
                                                                     file.MinecraftVersions.Any(version =>
                                                                         GetVersionFamily(version) == selectedFamily))
                .SelectMany(file => file.MinecraftVersions.DefaultIfEmpty(
                    CommonLanguageManager.Instance.recentPlay_unknownVersion.CurrentValue())
                    .Where(version => selectedFamily is null || GetVersionFamily(version) == selectedFamily)
                    .Select(version => (Version: version, File: file)))
                .GroupBy(item => item.Version)
                .OrderByDescending(group => MinecraftVersionKey.Parse(group.Key))
                .Select(group => new JavaResourceVersionGroup(group.Key,
                    group.Select(item => item.File).DistinctBy(file => file.Id)
                        .OrderByDescending(file => file.Published)
                        .ToArray()))
                .ToArray(), cancellation.Token);
            if (cancellation.IsCancellationRequested || _disposed) return;
            _allVersionGroups = groups;
            _nextVersionGroupIndex = 0;
            VersionGroups = [];
            LoadMoreVersionGroups();
            if (!_hasLocatedTargetVersionGroup && !string.IsNullOrWhiteSpace(Target.GameVersion) &&
                VersionGroups.FirstOrDefault(group => group.MinecraftVersion == Target.GameVersion) is { } targetGroup)
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

    private static string? GetVersionFamily(string version)
    {
        var match = Regex.Match(version, @"^(\d+)\.(\d+)");
        return match.Success ? $"{match.Groups[1].Value}.{match.Groups[2].Value}" : null;
    }

    private void AddScreenshots(IEnumerable<string?>? urls)
    {
        if (urls is null) return;
        foreach (var url in urls.Where(url => !string.IsNullOrWhiteSpace(url)).Distinct())
        {
            Screenshots.Add(url!);
            ScreenshotIndices.Add(ScreenshotIndices.Count);
        }

        OnPropertyChanged(nameof(HasScreenshots));
    }
}

public sealed partial class JavaResourceVersionGroup : ObservableObject
{
    private const int PageSize = 20;
    private readonly IReadOnlyList<JavaResourceFileItem> _files;

    public JavaResourceVersionGroup(string minecraftVersion, IReadOnlyList<JavaResourceFileItem> files)
    {
        Title = minecraftVersion;
        MinecraftVersion = minecraftVersion;
        _files = files;
        LoadMore();
    }

    public string Title { get; }
    public string MinecraftVersion { get; }
    public ObservableCollection<JavaResourceFileItem> VisibleFiles { get; } = [];
    public string FileCountText => string.Format(CommonLanguageManager.Instance.mod_fileCount.CurrentValue(),
        _files.Count);
    public bool HasMore => VisibleFiles.Count < _files.Count;
    public string LoadMoreText => string.Format(CommonLanguageManager.Instance.mod_loadMore.CurrentValue(),
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

public sealed record JavaResourceVersionFilter(string DisplayName, string? Family);

public sealed record JavaResourceFileItem(
    string Id,
    string DisplayName,
    string Details,
    string FileName,
    string DownloadUrl,
    long FileSize,
    DateTime Published,
    IReadOnlyList<string> MinecraftVersions)
{
    public static JavaResourceFileItem From(ResourceFile file)
    {
        var fileName = file.PrimaryFile?.FileName ?? string.Empty;
        return new JavaResourceFileItem(file.Id,
            string.IsNullOrWhiteSpace(file.Name) ? fileName : file.Name,
            FormatDetails(fileName, file.Published, file.ReleaseType), fileName,
            file.PrimaryFile?.Url ?? string.Empty, file.PrimaryFile?.Size ?? 0,
            file.Published ?? default, file.GameVersions.Where(IsMinecraftVersion).ToArray());
    }

    private static string FormatDetails(string fileName, DateTime? published,
        Iridium.Enums.Resources.ReleaseType releaseType)
    {
        return $"{fileName}·{RelativeTime.Format(published ?? default)}·{ReleaseType(releaseType)}";
    }

    private static string ReleaseType(Iridium.Enums.Resources.ReleaseType type)
    {
        return type switch
        {
            Iridium.Enums.Resources.ReleaseType.Beta =>
                CommonLanguageManager.Instance.mod_releaseTypeBeta.CurrentValue(),
            Iridium.Enums.Resources.ReleaseType.Alpha =>
                CommonLanguageManager.Instance.mod_releaseTypeAlpha.CurrentValue(),
            _ => CommonLanguageManager.Instance.mod_releaseTypeRelease.CurrentValue()
        };
    }

    private static bool IsMinecraftVersion(string version)
    {
        return Regex.IsMatch(version,
            @"^\d+\.\d+(?:\.\d+)?(?:-(?:snapshot|pre-release|pre\d+|rc\d+))?$", RegexOptions.IgnoreCase);
    }
}

public static class JavaResourceDownload
{
    public static async Task QuickDownloadAsync(TopLevel topLevel, JavaResourceDetailsTarget target)
    {
        var loading = new QuickDownloadLoadingDialogViewModel(
            string.Format(CommonLanguageManager.Instance.quickDownload_title.CurrentValue(),
                target.Definition.DisplayName));
        var loadingDialog = OverlayDialog
            .ShowCustomAsync<QuickDownloadLoadingDialog, QuickDownloadLoadingDialogViewModel,
                object?>(loading, topLevel.TryGetHostId(), new OverlayDialogOptions
            {
                Title = string.Format(CommonLanguageManager.Instance.quickDownload_title.CurrentValue(),
                    target.Definition.DisplayName), Buttons = DialogButton.None,
                CanLightDismiss = false, CanResize = false
            });
        try
        {
            Logger.Info(
                $"[Download] Loading quick-download files for {target.Definition.DisplayName} project {target.ProjectId} from {target.Source}.");
            IReadOnlyList<JavaResourceFileItem> files = target.Source switch
            {
                ModDetailsSource.Modrinth =>
                    (await IridiumResourceClients.Modrinth.GetFilesAsync(target.ProjectId))
                    .Select(file => JavaResourceFileItem.From(file.ToResourceFile())).ToArray(),
                ModDetailsSource.CurseForge => (await IridiumResourceClients.CurseForge.GetFilesAsync(
                        long.Parse(target.ProjectId)))
                    .Select(file => JavaResourceFileItem.From(file.ToResourceFile())).ToArray(),
                _ => []
            };
            if (files.Count == 0)
                throw new InvalidDataException(
                    CommonLanguageManager.Instance.quickDownload_noFiles.CurrentValue());
            loading.Close();
            await loadingDialog;

            var result = await OverlayDialog
                .ShowCustomAsync<JavaResourceInstallDialog, JavaResourceInstallDialogViewModel,
                    JavaResourceInstallDialogResult>(
                    new JavaResourceInstallDialogViewModel(target.Definition, files,
                        InstanceManager.Instance.Instances),
                    topLevel.TryGetHostId(), new OverlayDialogOptions
                    {
                        Title = string.Format(CommonLanguageManager.Instance.quickDownload_title.CurrentValue(),
                            target.Definition.DisplayName), Buttons = DialogButton.None,
                        CanLightDismiss = false, CanResize = false
                    });
            if (result?.File is not { } file) return;
            if (result.Destination == JavaResourceDownloadDestination.SaveAs)
            {
                await DownloadAsync(topLevel, target.Definition, file);
                return;
            }

            if (result.Instance is null) return;

            await InstallFromDialogAsync(topLevel, target.Definition, file, result.Instance, result.World);
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[Download] Quick download selection cancelled for project {target.ProjectId}: {exception}");
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

    public static async Task ShowInstallDialogAsync(TopLevel topLevel, JavaResourceDefinition definition,
        JavaResourceFileItem file)
    {
        var result = await OverlayDialog
            .ShowCustomAsync<JavaResourceInstallDialog, JavaResourceInstallDialogViewModel,
                JavaResourceInstallDialogResult>(
                new JavaResourceInstallDialogViewModel(definition, file, InstanceManager.Instance.Instances),
                topLevel.TryGetHostId(), new OverlayDialogOptions
                {
                    Title = string.Format(CommonLanguageManager.Instance.quickDownload_title.CurrentValue(),
                        definition.DisplayName), Buttons = DialogButton.None,
                    CanLightDismiss = false, CanResize = false
                });
        if (result?.File is not { } selectedFile) return;
        if (result.Destination == JavaResourceDownloadDestination.SaveAs)
        {
            await DownloadAsync(topLevel, definition, selectedFile);
            return;
        }

        if (result.Instance is null) return;

        await InstallFromDialogAsync(topLevel, definition, selectedFile, result.Instance, result.World);
    }

    private static async Task InstallFromDialogAsync(TopLevel topLevel, JavaResourceDefinition definition,
        JavaResourceFileItem file, MinecraftInstance instance, WorldSaveInfo? world)
    {
        if (definition.Kind == JavaResourceKind.Save)
        {
            InstallSave(topLevel, definition, file, instance.GetSpecialFolder(MinecraftSpecialFolder.SavesFolder));
            return;
        }

        string folder;
        if (definition.Kind == JavaResourceKind.DataPack)
        {
            if (world is null || await new WorldSaveService().IsWorldLockedAsync(world.FolderPath))
            {
                topLevel.Notice(CommonLanguageManager.Instance.javaResourceInstall_saveInUse.CurrentValue(),
                    NotificationType.Warning);
                return;
            }

            folder = Path.Combine(world.FolderPath, "datapacks");
        }
        else
        {
            var specialFolder = definition.Kind == JavaResourceKind.ResourcePack
                ? MinecraftSpecialFolder.ResourcePacksFolder
                : MinecraftSpecialFolder.ShaderPacksFolder;
            folder = instance.GetSpecialFolder(specialFolder);
        }

        Install(topLevel, definition, file, folder);
    }

    public static async Task DownloadAsync(TopLevel topLevel, JavaResourceDefinition definition,
        JavaResourceFileItem file)
    {
        var selected = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = string.Format(CommonLanguageManager.Instance.quickDownload_title.CurrentValue(),
                definition.DisplayName),
            SuggestedFileName = file.FileName,
            FileTypeChoices = [new FilePickerFileType(definition.DisplayName) { Patterns = Patterns(definition.Kind) }]
        });
        var destination = selected?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(destination)) return;

        Logger.Info($"[Download] Exporting {definition.DisplayName} {file.FileName} to {destination}.");
        StartDownload(topLevel, definition, file, destination);
    }

    public static void Install(TopLevel topLevel, JavaResourceDefinition definition, JavaResourceFileItem file,
        string folder)
    {
        Directory.CreateDirectory(folder);
        var fileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidDataException(
                CommonLanguageManager.Instance.javaResourceDownload_invalidResourceFileName.CurrentValue());
        StartDownload(topLevel, definition, file, Path.Combine(folder, fileName));
    }

    private static void InstallSave(TopLevel topLevel, JavaResourceDefinition definition, JavaResourceFileItem file,
        string savesFolder)
    {
        Directory.CreateDirectory(savesFolder);
        var fileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidDataException(
                CommonLanguageManager.Instance.javaResourceDownload_invalidSaveFileName.CurrentValue());
        var temporaryPath = Path.Combine(savesFolder, $".{Guid.NewGuid():N}.zip");
        StartDownload(topLevel, definition, file, temporaryPath, true);
    }

    internal static ManagedTask StartDownload(TopLevel topLevel, JavaResourceDefinition definition,
        JavaResourceFileItem file, string destination, bool extractSave = false)
    {
        Func<TaskExecutionContext, Task>? afterDownload = null;
        if (extractSave)
            afterDownload = async context =>
            {
                context.SetDescription(
                    CommonLanguageManager.Instance.javaResourceDownload_extractingSave.CurrentValue());
                await ExtractSaveAsync(destination, file.FileName, context.CancellationToken);
            };
        Logger.Info(
            $"[Download] Starting {definition.DisplayName} download {file.FileName} from {file.DownloadUrl} to {destination}; extractSave={extractSave}.");
        return DownloadTasks.Download(topLevel,
            string.Format(CommonLanguageManager.Instance.javaResourceDownload_taskName.CurrentValue(),
                definition.DisplayName, file.FileName),
            string.Format(CommonLanguageManager.Instance.javaResourceDownload_cancelDownload.CurrentValue(),
                definition.DisplayName), file.FileName, file.DownloadUrl, destination, file.FileSize,
            afterDownload, extractSave ? CommonLanguageManager.Instance.javaResourceDownload_saveInstalled.CurrentValue()
            : CommonLanguageManager.Instance.download_complete.CurrentValue());
    }

    private static IReadOnlyList<string> Patterns(JavaResourceKind kind)
    {
        return kind switch
        {
            JavaResourceKind.ResourcePack or JavaResourceKind.ShaderPack or JavaResourceKind.DataPack
                or JavaResourceKind.Save => ["*.zip"],
            _ => ["*.*"]
        };
    }

    private static Task ExtractSaveAsync(string archivePath, string fileName, CancellationToken cancellationToken)
    {
        return Task.Run(() => ExtractSave(archivePath, fileName, cancellationToken), cancellationToken);
    }

    private static void ExtractSave(string archivePath, string fileName, CancellationToken cancellationToken)
    {
        var savesFolder = Path.GetDirectoryName(archivePath) ?? throw new InvalidDataException(
            CommonLanguageManager.Instance.javaResourceDownload_invalidSaveDirectory.CurrentValue());
        var stagingFolder = Path.Combine(savesFolder, $".portal-{Guid.NewGuid():N}");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(stagingFolder);
            using var archive = ZipFile.OpenRead(archivePath);
            var stagingRoot = Path.GetFullPath(stagingFolder) + Path.DirectorySeparatorChar;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entryPath = Path.GetFullPath(Path.Combine(stagingFolder, entry.FullName));
                if (!entryPath.StartsWith(stagingRoot, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        CommonLanguageManager.Instance.javaResourceDownload_invalidSaveArchivePath.CurrentValue());
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(entryPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(entryPath)!);
                using var source = entry.Open();
                using var target = File.Create(entryPath);
                source.CopyToAsync(target, cancellationToken).GetAwaiter().GetResult();
            }

            cancellationToken.ThrowIfCancellationRequested();
            var worldFolder = File.Exists(Path.Combine(stagingFolder, "level.dat"))
                ? stagingFolder
                : Directory.EnumerateFiles(stagingFolder, "level.dat", SearchOption.AllDirectories)
                    .Select(Path.GetDirectoryName)
                    .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
            if (worldFolder is null)
                throw new InvalidDataException(
                    CommonLanguageManager.Instance.javaResourceDownload_invalidSaveArchive.CurrentValue());

            var baseName = Path.GetFileNameWithoutExtension(Path.GetFileName(fileName));
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "World";
            var destination = Path.Combine(savesFolder, baseName);
            for (var suffix = 2; Directory.Exists(destination); suffix++)
                destination = Path.Combine(savesFolder, $"{baseName} ({suffix})");
            Directory.Move(worldFolder, destination);
        }
        finally
        {
            if (Directory.Exists(stagingFolder)) Directory.Delete(stagingFolder, true);
            if (File.Exists(archivePath)) File.Delete(archivePath);
        }
    }
}

public sealed class ModpackDetailsPageViewModel(JavaResourceDetailsTarget target)
    : JavaResourceDetailsViewModel(target);

public sealed class ResourcePackDetailsPageViewModel(JavaResourceDetailsTarget target)
    : JavaResourceDetailsViewModel(target);

public sealed class ShaderPackDetailsPageViewModel(JavaResourceDetailsTarget target)
    : JavaResourceDetailsViewModel(target);

public sealed class DataPackDetailsPageViewModel(JavaResourceDetailsTarget target)
    : JavaResourceDetailsViewModel(target);

public sealed class SaveDetailsPageViewModel(JavaResourceDetailsTarget target) : JavaResourceDetailsViewModel(target);

public sealed class BedrockResourceDetailsPageViewModel(JavaResourceDetailsTarget target)
    : JavaResourceDetailsViewModel(target);
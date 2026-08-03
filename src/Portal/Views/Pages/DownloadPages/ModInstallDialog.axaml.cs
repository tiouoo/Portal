using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Components.Provider;
using MinecraftLaunch.Utilities;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Views.Pages.InstancePages;
using TioUi.Common.Interfaces;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Views.Pages.DownloadPages;

public partial class ModInstallDialog : UserControl
{
    public ModInstallDialog()
    {
        InitializeComponent();
    }

    private void Install_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as ModInstallDialogViewModel)?.Install(includeDependencies: true);

    private void SkipDependencies_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as ModInstallDialogViewModel)?.Install(includeDependencies: false);

    private void SaveAs_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as ModInstallDialogViewModel)?.SaveAs();

    private void Cancel_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as ModInstallDialogViewModel)?.Cancel();

    private void Dependency_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ModInstallDependencyItem item } || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        ModDetailsPage.Open(topLevel, item.Target, item.Name);
    }
}

public enum ModDownloadDestination
{
    Install,
    SaveAs
}

public sealed record ModInstallDialogResult(ModDownloadDestination Destination, MinecraftInstance? Instance,
    IReadOnlyList<ModVersionFileItem> Dependencies, ModVersionFileItem File);

public sealed record ModInstallInstanceItem(MinecraftInstance Instance, string Name, string Description);
public sealed record ModInstallDependencyItem(ModVersionFileItem File, ModDetailsTarget Target, string Name);

public partial class ModInstallDialogViewModel : ObservableObject, IDialogContext
{
    private readonly IReadOnlyList<ModInstallInstanceItem> _allInstances;
    private readonly IReadOnlyList<ModVersionFileItem> _files;
    private readonly ModrinthProvider _modrinth = new();
    private readonly CurseforgeProvider _curseforge = new();
    private int _dependencyLoadGeneration;

    public ModInstallDialogViewModel(ModVersionFileItem file, IEnumerable<MinecraftInstance> instances) : this([file], instances)
    {
    }

    public ModInstallDialogViewModel(IEnumerable<ModVersionFileItem> files, IEnumerable<MinecraftInstance> instances)
    {
        _files = files.OrderByDescending(item => item.Published).ToArray();
        File = _files.First();
        _allInstances = instances.Where(instance => instance.IsJava)
            .Select(instance => new ModInstallInstanceItem(instance, instance.InstanceName, instance.ShortDisplay))
            .ToArray();
        RefreshInstances();
        _ = LoadDependenciesAsync();
    }

    [ObservableProperty] public partial ModVersionFileItem File { get; set; }

    public string Metadata
    {
        get
        {
            var versions = string.Join("/", File.MinecraftVersions);

            var loaders = File.GroupKeys
                .Select(key => key.Loader == "通用" ? "通用加载器" : key.Loader)
                .Where(loader => !string.IsNullOrWhiteSpace(loader))
                .Distinct()
                .ToList();

            if (loaders.Count > 0)
            {
                var loaderText = string.Join("/", loaders);
                return $"适用于 {versions}·{loaderText}";
            }

            return $"适用于 {versions}";
        }
    }

    public ObservableCollection<ModInstallInstanceItem> Instances { get; } = [];
    public ObservableCollection<ModInstallDependencyItem> Dependencies { get; } = [];
    public bool HasNoInstances => Instances.Count == 0;
    public bool CanInstall => SelectedInstance is not null && HasCompatibleFile;
    public bool CanInstallWithDependencies => CanInstall && !IsLoadingDependencies && !HasDependencyLoadError;
    public bool HasDependencies => Dependencies.Count > 0;
    public bool ShowDependencyActions => IsLoadingDependencies || Dependencies.Count > 0 || HasDependencyLoadError;
    [ObservableProperty] public partial bool ShowAllInstances { get; set; }
    [ObservableProperty] public partial ModInstallInstanceItem? SelectedInstance { get; set; }
    [ObservableProperty] public partial bool HasCompatibleFile { get; set; } = true;
    [ObservableProperty] public partial bool IsLoadingDependencies { get; set; } = true;
    [ObservableProperty] public partial bool HasDependencyLoadError { get; set; }

    partial void OnSelectedInstanceChanged(ModInstallInstanceItem? value)
    {
        var compatibleFile = value is null ? _files.First() : FindLatestCompatibleFile(value.Instance);
        HasCompatibleFile = compatibleFile is not null;
        if (compatibleFile is not null) File = compatibleFile;
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanInstallWithDependencies));
    }

    partial void OnHasCompatibleFileChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanInstallWithDependencies));
    }

    partial void OnFileChanged(ModVersionFileItem value)
    {
        OnPropertyChanged(nameof(Metadata));
        _ = LoadDependenciesAsync();
    }

    partial void OnIsLoadingDependenciesChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInstallWithDependencies));
        OnPropertyChanged(nameof(ShowDependencyActions));
    }

    partial void OnHasDependencyLoadErrorChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInstallWithDependencies));
        OnPropertyChanged(nameof(ShowDependencyActions));
    }

    partial void OnShowAllInstancesChanged(bool value) => RefreshInstances();

    private void RefreshInstances()
    {
        var selectedPath = SelectedInstance?.Instance.InstanceFolderPath;
        var visibleInstances = ShowAllInstances
            ? _allInstances
            : _allInstances.Where(item => FindLatestCompatibleFile(item.Instance) is not null).ToArray();

        Instances.Clear();
        foreach (var instance in visibleInstances) Instances.Add(instance);
        SelectedInstance = Instances.FirstOrDefault(item => item.Instance.InstanceFolderPath == selectedPath) ??
                           Instances.FirstOrDefault(item =>
                               item.Instance.InstanceFolderPath == Data.UiProperty.LastModInstallInstancePath) ??
                           Instances.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoInstances));
    }

    private ModVersionFileItem? FindLatestCompatibleFile(MinecraftInstance instance) => _files.FirstOrDefault(file =>
        IsCompatible(file, instance));

    private static bool IsCompatible(ModVersionFileItem file, MinecraftInstance instance)
    {
        if (!file.MinecraftVersions.Contains(instance.VersionId, StringComparer.OrdinalIgnoreCase)) return false;
        var compatibleLoaders = file.GroupKeys.Select(key => key.Loader).Where(loader => loader != "通用").ToHashSet();
        return compatibleLoaders.Count == 0 || instance.MinecraftEntry is ModifiedMinecraftEntry entry &&
            entry.ModLoaders.Any(loader => compatibleLoaders.Contains(LoaderName(loader.Type)));
    }

    private static string LoaderName(MinecraftLaunch.Base.Enums.ModLoaderType loader) => loader switch
    {
        MinecraftLaunch.Base.Enums.ModLoaderType.NeoForge => "NeoForge",
        MinecraftLaunch.Base.Enums.ModLoaderType.Forge => "Forge",
        MinecraftLaunch.Base.Enums.ModLoaderType.Fabric => "Fabric",
        MinecraftLaunch.Base.Enums.ModLoaderType.Quilt => "Quilt",
        _ => string.Empty
    };

    private async Task LoadDependenciesAsync()
    {
        var generation = Interlocked.Increment(ref _dependencyLoadGeneration);
        var file = File;
        Dependencies.Clear();
        OnPropertyChanged(nameof(HasDependencies));
        IsLoadingDependencies = true;
        HasDependencyLoadError = false;
        try
        {
            IReadOnlyList<ModVersionFileItem> dependencies = file.Source switch
            {
                ModDetailsSource.Modrinth => await LoadModrinthDependenciesAsync(file),
                ModDetailsSource.CurseForge => await LoadCurseForgeDependenciesAsync(file),
                _ => []
            };
            var items = await Task.WhenAll(dependencies.Select(async dependency => new ModInstallDependencyItem(dependency,
                CreateDetailsTarget(file, dependency), await GetDependencyNameAsync(dependency))));
            if (generation != _dependencyLoadGeneration) return;
            foreach (var item in items) Dependencies.Add(item);
            OnPropertyChanged(nameof(HasDependencies));
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            if (generation == _dependencyLoadGeneration) HasDependencyLoadError = true;
        }
        finally
        {
            if (generation == _dependencyLoadGeneration)
            {
                IsLoadingDependencies = false;
                OnPropertyChanged(nameof(ShowDependencyActions));
            }
        }
    }

    private async Task<IReadOnlyList<ModVersionFileItem>> LoadModrinthDependenciesAsync(ModVersionFileItem file)
    {
        var dependencies = file.Dependencies.ToList();
        try
        {
            var declaredProjects = dependencies.Select(dependency => dependency.ProjectId).ToHashSet();
            var modIds = await ReadNeoForgeRequiredModIdsAsync(file);
            foreach (var modId in modIds)
            {
                var project = (await _modrinth.SearchAsync(modId)).FirstOrDefault(candidate =>
                    string.Equals(candidate.Slug, modId, StringComparison.OrdinalIgnoreCase));
                if (project is not null && declaredProjects.Add(project.ProjectId))
                    dependencies.Add(new ModFileDependency(project.ProjectId, project.Name));
            }
        }
        catch (Exception exception)
        {
            Logger.Warning($"[ModInstall] Failed to inspect NeoForge requirements for {file.FileName}: {exception}");
            // Platform metadata remains the primary source when the archive cannot be inspected.
        }

        var files = await Task.WhenAll(dependencies.Select(dependency => LoadModrinthDependencyAsync(file, dependency)));
        return files.OfType<ModVersionFileItem>().DistinctBy(file => file.Id).ToArray();
    }

    private async Task<IReadOnlyList<string>> ReadNeoForgeRequiredModIdsAsync(ModVersionFileItem file)
    {
        if (!file.GroupKeys.Any(key => key.Loader == "NeoForge")) return [];

        await using var stream = await HttpUtil.Client.GetStreamAsync(file.DownloadUrl);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("META-INF/neoforge.mods.toml") ?? archive.GetEntry("META-INF/mods.toml");
        if (entry is null) return [];

        using var reader = new StreamReader(entry.Open());
        var metadata = await reader.ReadToEndAsync();
        return Regex.Matches(metadata, @"(?ms)^\s*\[\[dependencies\.[^\]]+\]\](?<body>.*?)(?=^\s*\[\[|\z)")
            .Select(match => match.Groups["body"].Value)
            .Where(body => Regex.IsMatch(body, "(?m)^\\s*(?:type\\s*=\\s*\\\"required\\\"|mandatory\\s*=\\s*true)"))
            .Select(body => Regex.Match(body, "(?m)^\\s*modId\\s*=\\s*\\\"(?<id>[^\\\"]+)\\\"").Groups["id"].Value)
            .Where(id => !string.IsNullOrWhiteSpace(id) && id is not "minecraft" and not "neoforge" and not "forge")
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<ModVersionFileItem?> LoadModrinthDependencyAsync(ModVersionFileItem file, ModFileDependency dependency)
    {
        if (!string.IsNullOrWhiteSpace(dependency.VersionId))
        {
            var fixedVersion = ModVersionFileItem.From(await _modrinth.GetModFileByVersionIdAsync(dependency.VersionId));
            if (IsCompatible(file, fixedVersion)) return fixedVersion;
        }

        if (string.IsNullOrWhiteSpace(dependency.ProjectId)) return null;
        var files = await _modrinth.GetModFilesByProjectIdAsync(dependency.ProjectId);
        return files.Select(ModVersionFileItem.From).Where(candidate => IsCompatible(file, candidate))
            .OrderByDescending(candidate => candidate.MinecraftVersions.Count(version => file.MinecraftVersions.Contains(version)))
            .ThenByDescending(candidate => candidate.Id).FirstOrDefault();
    }

    private async Task<IReadOnlyList<ModVersionFileItem>> LoadCurseForgeDependenciesAsync(ModVersionFileItem file)
    {
        var ids = file.Dependencies.Select(dependency => long.Parse(dependency.ProjectId)).Distinct().ToArray();
        if (ids.Length == 0) return [];

        var projects = await _curseforge.GetResourcesByModIdsAsync(ids);
        var candidates = await Task.WhenAll(projects.Select(async project =>
        {
            var files = await _curseforge.GetModFilesAsync(project.Id);
            return files.Select(ModVersionFileItem.From).Where(candidate => IsCompatible(file, candidate))
                .OrderByDescending(candidate => candidate.Id).FirstOrDefault();
        }));
        return candidates.OfType<ModVersionFileItem>().DistinctBy(file => file.Id).ToArray();
    }

    private static bool IsCompatible(ModVersionFileItem file, ModVersionFileItem candidate)
    {
        if (!candidate.MinecraftVersions.Intersect(file.MinecraftVersions).Any()) return false;

        var selectedLoaders = file.GroupKeys.Select(key => key.Loader).Where(loader => loader != "通用").Distinct().ToArray();
        return selectedLoaders.Length == 0 || candidate.GroupKeys.Any(key =>
            key.Loader == "通用" || selectedLoaders.Contains(key.Loader));
    }

    private static ModDetailsTarget CreateDetailsTarget(ModVersionFileItem file, ModVersionFileItem dependency)
    {
        var gameVersion = file.MinecraftVersions.Intersect(dependency.MinecraftVersions).FirstOrDefault() ?? string.Empty;
        var selectedLoaders = file.GroupKeys.Select(key => key.Loader).Where(loader => loader != "通用").ToHashSet();
        var loader = dependency.GroupKeys.Select(key => key.Loader)
            .FirstOrDefault(selectedLoaders.Contains) ?? dependency.GroupKeys.FirstOrDefault()?.Loader;
        return new ModDetailsTarget(dependency.Source, dependency.ProjectId, gameVersion, ToModLoaderType(loader));
    }

    private async Task<string> GetDependencyNameAsync(ModVersionFileItem dependency)
    {
        if (dependency.Source == ModDetailsSource.Modrinth)
        {
            var modrinthProject = await _modrinth.SearchByProjectIdAsync(dependency.ProjectId);
            return modrinthProject.Name;
        }

        var curseForgeProject = (await _curseforge.GetResourcesByModIdsAsync([long.Parse(dependency.ProjectId)])).First();
        return curseForgeProject.Name;
    }

    private static ModLoaderType ToModLoaderType(string? loader) => loader switch
    {
        "NeoForge" => ModLoaderType.NeoForge,
        "Forge" => ModLoaderType.Forge,
        "Fabric" => ModLoaderType.Fabric,
        "Quilt" => ModLoaderType.Quilt,
        _ => ModLoaderType.Any
    };

    public void Install(bool includeDependencies)
    {
        if (SelectedInstance is not null)
            Data.UiProperty.LastModInstallInstancePath = SelectedInstance.Instance.InstanceFolderPath;
        RequestClose?.Invoke(this, new ModInstallDialogResult(ModDownloadDestination.Install, SelectedInstance?.Instance,
            includeDependencies ? Dependencies.Select(dependency => dependency.File).ToArray() : [], File));
    }

    public void SaveAs() => RequestClose?.Invoke(this, new ModInstallDialogResult(ModDownloadDestination.SaveAs, null, [],
        _files.First()));
    public void Cancel() => RequestClose?.Invoke(this, null);
    public void Close() => Cancel();
    public event EventHandler<object?>? RequestClose;
}

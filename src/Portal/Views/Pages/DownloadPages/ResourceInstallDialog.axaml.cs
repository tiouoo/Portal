using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Models;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using TioUi.Common.Interfaces;

namespace Portal.Views.Pages.DownloadPages;

public partial class ResourceInstallDialog : UserControl
{
    public ResourceInstallDialog()
    {
        InitializeComponent();
    }

    private void Install_Click(object? sender, RoutedEventArgs e)
    {
        (DataContext as ResourceInstallDialogViewModel)?.Install();
    }

    private void SaveAs_Click(object? sender, RoutedEventArgs e)
    {
        (DataContext as ResourceInstallDialogViewModel)?.SaveAs();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        (DataContext as ResourceInstallDialogViewModel)?.Cancel();
    }
}

public enum ResourceDownloadDestination
{
    Install,
    SaveAs
}

public sealed record ResourceInstallDialogResult(
    ResourceDownloadDestination Destination,
    MinecraftInstance? Instance,
    WorldSaveInfo? World,
    ResourceVersionFileItem? File);

public sealed record ResourceInstallInstanceItem(MinecraftInstance Instance, string Name, string Description);

public sealed record ResourceInstallWorldItem(WorldSaveInfo World, string Name, string Description);

public partial class ResourceInstallDialogViewModel : ObservableObject, IDialogContext
{
    private readonly IReadOnlyList<ResourceInstallInstanceItem> _allInstances;

    private readonly IReadOnlyList<ResourceVersionFileItem> _files;
    private readonly WorldSaveService _worldSaveService = new();
    private CancellationTokenSource? _worldLoadCancellation;

    public ResourceInstallDialogViewModel(ResourceDefinition definition, ResourceVersionFileItem file,
        IEnumerable<MinecraftInstance> instances) : this(definition, [file], instances)
    {
    }

    public ResourceInstallDialogViewModel(ResourceDefinition definition,
        IEnumerable<ResourceVersionFileItem> files,
        IEnumerable<MinecraftInstance> instances)
    {
        Definition = definition;
        _files = files.OrderByDescending(item => item.Published).ToArray();
        File = _files.FirstOrDefault();
        _allInstances = instances.Where(instance => instance.IsJava)
            .Select(instance =>
                new ResourceInstallInstanceItem(instance, instance.InstanceName, instance.ShortDisplay))
            .ToArray();
        RefreshInstances();
    }

    public ResourceDefinition Definition { get; }
    [ObservableProperty] public partial ResourceVersionFileItem? File { get; set; }
    public string Metadata => File is null
        ? CommonLanguageManager.Instance.javaResourceInstall_noCompatibleVersion.CurrentValue()
        : string.Format(CommonLanguageManager.Instance.javaResourceInstall_appliesTo.CurrentValue(),
            string.Join("/", File.MinecraftVersions));
    public bool IsDataPack => Definition.Kind == ResourceKind.DataPack;
    public ObservableCollection<ResourceInstallInstanceItem> Instances { get; } = [];
    public ObservableCollection<ResourceInstallWorldItem> Worlds { get; } = [];
    public bool HasNoInstances => Instances.Count == 0;
    public bool HasNoWorlds => IsDataPack && !IsLoadingWorlds && Worlds.Count == 0;

    public bool CanInstall =>
        File is not null && SelectedInstance is not null && (!IsDataPack || SelectedWorld is not null);

    [ObservableProperty] public partial bool ShowAllInstances { get; set; }
    [ObservableProperty] public partial ResourceInstallInstanceItem? SelectedInstance { get; set; }
    [ObservableProperty] public partial ResourceInstallWorldItem? SelectedWorld { get; set; }
    [ObservableProperty] public partial bool IsLoadingWorlds { get; set; }

    public void Close()
    {
        Cancel();
    }

    public event EventHandler<object?>? RequestClose;

    partial void OnShowAllInstancesChanged(bool value)
    {
        RefreshInstances();
    }

    partial void OnSelectedInstanceChanged(ResourceInstallInstanceItem? value)
    {
        File = value is null ? _files.FirstOrDefault() : FindLatestCompatibleFile(value.Instance);
        OnPropertyChanged(nameof(CanInstall));
        if (IsDataPack) _ = LoadWorldsAsync(value?.Instance);
    }

    partial void OnFileChanged(ResourceVersionFileItem? value)
    {
        OnPropertyChanged(nameof(Metadata));
        OnPropertyChanged(nameof(CanInstall));
    }

    partial void OnSelectedWorldChanged(ResourceInstallWorldItem? value)
    {
        OnPropertyChanged(nameof(CanInstall));
    }

    partial void OnIsLoadingWorldsChanged(bool value)
    {
        OnPropertyChanged(nameof(HasNoWorlds));
        OnPropertyChanged(nameof(CanInstall));
    }

    private void RefreshInstances()
    {
        var selectedPath = SelectedInstance?.Instance.InstanceFolderPath;
        var compatible = ShowAllInstances
            ? _allInstances
            : _allInstances.Where(item => FindLatestCompatibleFile(item.Instance) is not null).ToArray();
        Instances.Clear();
        foreach (var instance in compatible) Instances.Add(instance);
        SelectedInstance = Instances.FirstOrDefault(item =>
            string.Equals(item.Instance.InstanceFolderPath, selectedPath,
                StringComparison.OrdinalIgnoreCase)) ?? Instances.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoInstances));
        OnPropertyChanged(nameof(CanInstall));
    }

    private ResourceVersionFileItem? FindLatestCompatibleFile(MinecraftInstance instance)
    {
        return _files.FirstOrDefault(file =>
            file.MinecraftVersions.Count == 0 || file.MinecraftVersions.Contains(instance.VersionId,
                StringComparer.OrdinalIgnoreCase));
    }

    private async Task LoadWorldsAsync(MinecraftInstance? instance)
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _worldLoadCancellation, cancellation);
        previous?.Cancel();
        Worlds.Clear();
        SelectedWorld = null;
        if (instance is null)
        {
            OnPropertyChanged(nameof(HasNoWorlds));
            return;
        }

        IsLoadingWorlds = true;
        try
        {
            var worlds = await _worldSaveService.ScanAsync(instance, cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            foreach (var world in worlds.Where(world => !world.IsLocked))
            {
                var name = string.IsNullOrWhiteSpace(world.LevelName) ? world.FolderName : world.LevelName;
                var description = string.Format(
                    CommonLanguageManager.Instance.javaResourceInstall_worldDescription.CurrentValue(), world.FolderName,
                    world.Version ?? CommonLanguageManager.Instance.recentPlay_unknownVersion.CurrentValue(),
                    world.DataPackArchiveCount);
                Worlds.Add(new ResourceInstallWorldItem(world, name, description));
            }

            SelectedWorld = Worlds.FirstOrDefault();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Logger.Debug("[Download] World list loading cancelled.");
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
        }
        finally
        {
            if (ReferenceEquals(_worldLoadCancellation, cancellation)) _worldLoadCancellation = null;
            cancellation.Dispose();
            IsLoadingWorlds = false;
            OnPropertyChanged(nameof(HasNoWorlds));
        }
    }

    public void Install()
    {
        RequestClose?.Invoke(this,
            new ResourceInstallDialogResult(ResourceDownloadDestination.Install, SelectedInstance?.Instance,
                SelectedWorld?.World, File));
    }

    public void SaveAs()
    {
        RequestClose?.Invoke(this,
            new ResourceInstallDialogResult(ResourceDownloadDestination.SaveAs, null, null,
                _files.FirstOrDefault()));
    }

    public void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }
}
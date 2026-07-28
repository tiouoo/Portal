using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using TioUi.Common.Interfaces;

namespace Portal.Views.Pages.DownloadPages;

public partial class JavaResourceInstallDialog : UserControl
{
    public JavaResourceInstallDialog() => InitializeComponent();
    private void Install_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as JavaResourceInstallDialogViewModel)?.Install();
    private void SaveAs_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as JavaResourceInstallDialogViewModel)?.SaveAs();
    private void Cancel_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as JavaResourceInstallDialogViewModel)?.Cancel();
}

public enum JavaResourceDownloadDestination { Install, SaveAs }
public sealed record JavaResourceInstallDialogResult(JavaResourceDownloadDestination Destination,
    MinecraftInstance? Instance, WorldSaveInfo? World, JavaResourceFileItem? File);
public sealed record JavaResourceInstallInstanceItem(MinecraftInstance Instance, string Name, string Description);
public sealed record JavaResourceInstallWorldItem(WorldSaveInfo World, string Name, string Description);

public partial class JavaResourceInstallDialogViewModel : ObservableObject, IDialogContext
{
    private readonly IReadOnlyList<JavaResourceInstallInstanceItem> _allInstances;
    private readonly WorldSaveService _worldSaveService = new();
    private CancellationTokenSource? _worldLoadCancellation;

    private readonly IReadOnlyList<JavaResourceFileItem> _files;

    public JavaResourceInstallDialogViewModel(JavaResourceDefinition definition, JavaResourceFileItem file,
        IEnumerable<MinecraftInstance> instances) : this(definition, [file], instances)
    {
    }

    public JavaResourceInstallDialogViewModel(JavaResourceDefinition definition, IEnumerable<JavaResourceFileItem> files,
        IEnumerable<MinecraftInstance> instances)
    {
        Definition = definition;
        _files = files.OrderByDescending(item => item.Published).ToArray();
        File = _files.FirstOrDefault();
        _allInstances = instances.Where(instance => instance.IsJava)
            .Select(instance => new JavaResourceInstallInstanceItem(instance, instance.InstanceName, instance.ShortDisplay))
            .ToArray();
        RefreshInstances();
    }

    public JavaResourceDefinition Definition { get; }
    [ObservableProperty] public partial JavaResourceFileItem? File { get; set; }
    public string Metadata => File is null ? "所选实例没有兼容版本" : $"适用于 {string.Join("/", File.MinecraftVersions)}";
    public bool IsDataPack => Definition.Kind == JavaResourceKind.DataPack;
    public ObservableCollection<JavaResourceInstallInstanceItem> Instances { get; } = [];
    public ObservableCollection<JavaResourceInstallWorldItem> Worlds { get; } = [];
    public bool HasNoInstances => Instances.Count == 0;
    public bool HasNoWorlds => IsDataPack && !IsLoadingWorlds && Worlds.Count == 0;
    public bool CanInstall => File is not null && SelectedInstance is not null && (!IsDataPack || SelectedWorld is not null);
    [ObservableProperty] public partial bool ShowAllInstances { get; set; }
    [ObservableProperty] public partial JavaResourceInstallInstanceItem? SelectedInstance { get; set; }
    [ObservableProperty] public partial JavaResourceInstallWorldItem? SelectedWorld { get; set; }
    [ObservableProperty] public partial bool IsLoadingWorlds { get; set; }

    partial void OnShowAllInstancesChanged(bool value) => RefreshInstances();

    partial void OnSelectedInstanceChanged(JavaResourceInstallInstanceItem? value)
    {
        File = value is null ? _files.FirstOrDefault() : FindLatestCompatibleFile(value.Instance);
        OnPropertyChanged(nameof(CanInstall));
        if (IsDataPack) _ = LoadWorldsAsync(value?.Instance);
    }

    partial void OnFileChanged(JavaResourceFileItem? value)
    {
        OnPropertyChanged(nameof(Metadata));
        OnPropertyChanged(nameof(CanInstall));
    }

    partial void OnSelectedWorldChanged(JavaResourceInstallWorldItem? value) =>
        OnPropertyChanged(nameof(CanInstall));

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

    private JavaResourceFileItem? FindLatestCompatibleFile(MinecraftInstance instance) => _files.FirstOrDefault(file =>
        file.MinecraftVersions.Count == 0 || file.MinecraftVersions.Contains(instance.VersionId,
            StringComparer.OrdinalIgnoreCase));

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
                var description = $"{world.FolderName}·{world.Version ?? "未知版本"}·{world.DataPackArchiveCount} 个数据包";
                Worlds.Add(new JavaResourceInstallWorldItem(world, name, description));
            }
            SelectedWorld = Worlds.FirstOrDefault();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch
        {
        }
        finally
        {
            if (ReferenceEquals(_worldLoadCancellation, cancellation)) _worldLoadCancellation = null;
            cancellation.Dispose();
            IsLoadingWorlds = false;
            OnPropertyChanged(nameof(HasNoWorlds));
        }
    }

    public void Install() => RequestClose?.Invoke(this,
        new JavaResourceInstallDialogResult(JavaResourceDownloadDestination.Install, SelectedInstance?.Instance,
            SelectedWorld?.World, File));
    public void SaveAs() => RequestClose?.Invoke(this,
        new JavaResourceInstallDialogResult(JavaResourceDownloadDestination.SaveAs, null, null,
            _files.FirstOrDefault()));
    public void Cancel() => RequestClose?.Invoke(this, null);
    public void Close() => Cancel();
    public event EventHandler<object?>? RequestClose;
}

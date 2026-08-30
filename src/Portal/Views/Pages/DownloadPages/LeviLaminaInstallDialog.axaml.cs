using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia;
using Portal.Core.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using TioUi.Common.Interfaces;

namespace Portal.Views.Pages.DownloadPages;

public partial class LeviLaminaInstallDialog : UserControl
{
    public LeviLaminaInstallDialog()
    {
        InitializeComponent();
    }

    private void Confirm_OnClick(object? sender, RoutedEventArgs e) =>
        (DataContext as LeviLaminaInstallDialogViewModel)?.Confirm();

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) =>
        (DataContext as LeviLaminaInstallDialogViewModel)?.Cancel();
}

internal sealed record LeviLaminaInstallResult(
    string Version,
    MinecraftInstance Instance,
    IReadOnlyList<LeviDependency> Dependencies);

public sealed record LeviLaminaInstanceItem(
    MinecraftInstance Instance,
    string InstanceName,
    string ShortDisplay,
    bool IsLoaderInstalled);

public sealed partial class LeviLaminaDependencyItem : ObservableObject
{
    public LeviLaminaDependencyItem(string key, string constraint, string? version, bool installed, int level)
    {
        Key = key;
        Constraint = constraint;
        Version = version;
        Level = level;
        Name = key.Split('#')[0].Split('/').Last();
        Installed = installed;
    }

    public string Key { get; }
    public string Name { get; }
    public string Constraint { get; }
    public string? Version { get; }
    public int Level { get; }
    public Thickness IndentMargin => new(Level * 20, 2, 0, 2);
    [ObservableProperty] public partial bool Installed { get; set; }

    public string StatusText => Installed
        ? DownloadsLanguageManager.Instance.levilaminasearchpage_installed.CurrentValue()
        : DownloadsLanguageManager.Instance.levilaminasearchpage_willInstall.CurrentValue();

    public IBrush StatusBrush => Installed ? Brushes.LimeGreen : Brushes.DarkOrange;

    partial void OnInstalledChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
    }
}

public partial class LeviLaminaInstallDialogViewModel : ObservableObject, IDialogContext
{
    private int _dependencyRefreshGeneration;
    private CancellationTokenSource? _dependencyCancellation;
    private bool _dependenciesLoaded;

    public LeviLaminaInstallDialogViewModel(LeviLaminaSearchResultItem item)
    {
        Item = item;
        Versions = item.Package.Variants.TryGetValue("client", out var variant)
            ? variant.Versions.Keys.OrderByDescending(x => x).ToArray()
            : [];
        Instances = new ObservableCollection<LeviLaminaInstanceItem>(InstanceManager.Instance.Instances
            .Where(x => x.IsBedrock).Select(x =>
                new LeviLaminaInstanceItem(x, x.InstanceName, x.ShortDisplay,
                    LeviLaminaInstallState.IsLoaderInstalled(x))));
        SelectedVersion = Versions.FirstOrDefault();
        SelectedInstance = Instances.FirstOrDefault();
        _ = RefreshDependenciesAsync();
    }

    public LeviLaminaSearchResultItem Item { get; }
    public IReadOnlyList<string> Versions { get; }
    public ObservableCollection<LeviLaminaInstanceItem> Instances { get; }
    public bool HasInstances => Instances.Count > 0;
    public string Metadata => Item.Metadata;
    public ObservableCollection<LeviLaminaDependencyItem> Dependencies { get; } = [];
    public bool HasDependencies => Dependencies.Count > 0;
    public bool HasNoDependencies => _dependenciesLoaded && !IsLoadingDependencies &&
                                     !HasDependencyLoadError && Dependencies.Count == 0;
    [ObservableProperty] public partial bool HasDependencyLoadError { get; set; }
    [ObservableProperty] public partial bool IsLoadingDependencies { get; set; }
    [ObservableProperty] public partial string? SelectedVersion { get; set; }
    [ObservableProperty] public partial LeviLaminaInstanceItem? SelectedInstance { get; set; }
    public bool CanConfirm => SelectedVersion is not null && SelectedInstance is not null &&
                              _dependenciesLoaded && !IsLoadingDependencies && !HasDependencyLoadError;

    partial void OnSelectedVersionChanged(string? value)
    {
        _ = RefreshDependenciesAsync();
        OnPropertyChanged(nameof(CanConfirm));
    }

    partial void OnSelectedInstanceChanged(LeviLaminaInstanceItem? value)
    {
        _ = RefreshDependenciesAsync();
        OnPropertyChanged(nameof(CanConfirm));
    }

    private async Task RefreshDependenciesAsync()
    {
        var generation = ++_dependencyRefreshGeneration;
        _dependencyCancellation?.Cancel();
        _dependencyCancellation?.Dispose();
        _dependencyCancellation = new CancellationTokenSource();
        var cancellationToken = _dependencyCancellation.Token;
        Dependencies.Clear();
        _dependenciesLoaded = false;
        HasDependencyLoadError = false;
        IsLoadingDependencies = false;
        if (SelectedVersion is null || !Item.Package.Variants.TryGetValue("client", out var variant) ||
            !variant.Versions.TryGetValue(SelectedVersion, out var version))
        {
            NotifyDependencyStateChanged();
            return;
        }

        try
        {
            IsLoadingDependencies = true;
            NotifyDependencyStateChanged();
            var packages = await LeviLaminaDownloadService.LoadLiprAsync(cancellationToken);
            if (generation != _dependencyRefreshGeneration) return;

            var dependencies = await LeviLaminaDownloadService.ExpandToothDependenciesAsync(
                Item.Key, SelectedVersion, packages, cancellationToken);
            if (generation != _dependencyRefreshGeneration) return;
            foreach (var dependency in dependencies)
            {
                if (generation != _dependencyRefreshGeneration) return;
                var installed = SelectedInstance is not null &&
                                LeviLaminaInstallState.IsDependencyInstalled(SelectedInstance.Instance,
                                    dependency.Key, dependency.Version);
                Dependencies.Add(new LeviLaminaDependencyItem(dependency.Key, dependency.Constraint,
                    dependency.Version, installed, dependency.Level));
            }

            _dependenciesLoaded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            if (generation == _dependencyRefreshGeneration)
                HasDependencyLoadError = true;
        }

        if (generation == _dependencyRefreshGeneration)
            IsLoadingDependencies = false;
        NotifyDependencyStateChanged();
    }

    private void NotifyDependencyStateChanged()
    {
        OnPropertyChanged(nameof(HasDependencies));
        OnPropertyChanged(nameof(HasNoDependencies));
        OnPropertyChanged(nameof(CanConfirm));
    }

    public event EventHandler<object?>? RequestClose;
    public void Close() => Cancel();

    public void Confirm()
    {
        if (!CanConfirm || SelectedVersion is null || SelectedInstance is not { } selected) return;
        var dependencies = Dependencies.Select(item =>
            new LeviDependency(item.Key, item.Constraint, item.Version, item.Level)).ToArray();
        _dependencyCancellation?.Cancel();
        RequestClose?.Invoke(this,
            new LeviLaminaInstallResult(SelectedVersion, selected.Instance, dependencies));
    }

    public void Cancel()
    {
        _dependencyCancellation?.Cancel();
        RequestClose?.Invoke(this, null);
    }
}

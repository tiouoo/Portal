using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using Avalonia.Media;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Localization;
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

public sealed record LeviLaminaInstallResult(string Version, MinecraftInstance Instance);

public sealed record LeviLaminaInstanceItem(
    MinecraftInstance Instance,
    string InstanceName,
    string ShortDisplay,
    bool IsLoaderInstalled);

public sealed partial class LeviLaminaDependencyItem : ObservableObject
{
    public LeviLaminaDependencyItem(string key, string constraint, bool installed)
    {
        Key = key;
        Constraint = constraint;
        Name = key.Split('#')[0].Split('/').Last();
        Installed = installed;
    }

    public string Key { get; }
    public string Name { get; }
    public string Constraint { get; }
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
    public LeviLaminaInstallDialogViewModel(LeviLaminaSearchResultItem item)
    {
        Item = item;
        Versions = item.Package.Variants.TryGetValue("client", out var variant)
            ? variant.Versions.Keys.OrderByDescending(x => x).ToArray()
            : [];
        Instances = new ObservableCollection<LeviLaminaInstanceItem>(InstanceManager.Instance.Instances
            .Where(x => x.IsBedrock && LeviLaminaInstallState.IsLoaderInstalled(x)).Select(x =>
                new LeviLaminaInstanceItem(x, x.InstanceName, x.ShortDisplay, true)));
        SelectedVersion = Versions.FirstOrDefault();
        SelectedInstance = Instances.FirstOrDefault(x => x.IsLoaderInstalled);
        RefreshDependencies();
    }

    public LeviLaminaSearchResultItem Item { get; }
    public IReadOnlyList<string> Versions { get; }
    public ObservableCollection<LeviLaminaInstanceItem> Instances { get; }
    public bool HasInstances => Instances.Count > 0;
    public string Metadata => Item.Metadata;
    public ObservableCollection<LeviLaminaDependencyItem> Dependencies { get; } = [];
    public bool HasDependencies => Dependencies.Count > 0;
    public bool HasNoDependencies => Dependencies.Count == 0;
    [ObservableProperty] public partial string? SelectedVersion { get; set; }
    [ObservableProperty] public partial LeviLaminaInstanceItem? SelectedInstance { get; set; }
    public bool CanConfirm => SelectedVersion is not null && SelectedInstance is { IsLoaderInstalled: true };

    partial void OnSelectedVersionChanged(string? value)
    {
        RefreshDependencies();
        OnPropertyChanged(nameof(CanConfirm));
    }

    partial void OnSelectedInstanceChanged(LeviLaminaInstanceItem? value)
    {
        RefreshDependencies();
        OnPropertyChanged(nameof(CanConfirm));
    }

    private void RefreshDependencies()
    {
        Dependencies.Clear();
        if (SelectedVersion is null || !Item.Package.Variants.TryGetValue("client", out var variant) ||
            !variant.Versions.TryGetValue(SelectedVersion, out var version))
        {
            OnPropertyChanged(nameof(HasDependencies));
            OnPropertyChanged(nameof(HasNoDependencies));
            return;
        }

        foreach (var dependency in version.Dependencies)
        {
            var installed = SelectedInstance is not null &&
                            LeviLaminaInstallState.IsDependencyInstalled(SelectedInstance.Instance, dependency.Key,
                                dependency.Value);
            Dependencies.Add(new LeviLaminaDependencyItem(dependency.Key, dependency.Value, installed));
        }

        OnPropertyChanged(nameof(HasDependencies));
        OnPropertyChanged(nameof(HasNoDependencies));
    }

    public event EventHandler<object?>? RequestClose;
    public void Close() => Cancel();

    public void Confirm()
    {
        if (SelectedVersion is not null && SelectedInstance is { IsLoaderInstalled: true } selected)
            RequestClose?.Invoke(this, new LeviLaminaInstallResult(SelectedVersion, selected.Instance));
    }

    public void Cancel() => RequestClose?.Invoke(this, null);
}
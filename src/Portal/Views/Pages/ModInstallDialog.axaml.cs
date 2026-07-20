using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Base.Models.Game;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using TioUi.Common.Interfaces;

namespace Portal.Views.Pages;

public partial class ModInstallDialog : UserControl
{
    public ModInstallDialog()
    {
        InitializeComponent();
    }

    private void Install_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as ModInstallDialogViewModel)?.Install();

    private void SaveAs_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as ModInstallDialogViewModel)?.SaveAs();

    private void Cancel_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as ModInstallDialogViewModel)?.Cancel();
}

public enum ModDownloadDestination
{
    Install,
    SaveAs
}

public sealed record ModInstallDialogResult(ModDownloadDestination Destination, MinecraftInstance? Instance);

public sealed record ModInstallInstanceItem(MinecraftInstance Instance, string Name, string Description);

public partial class ModInstallDialogViewModel : ObservableObject, IDialogContext
{
    private readonly IReadOnlyList<ModInstallInstanceItem> _allInstances;

    public ModInstallDialogViewModel(ModVersionFileItem file, IEnumerable<MinecraftInstance> instances)
    {
        File = file;
        _allInstances = instances.Where(instance => instance.IsJava)
            .Select(instance => new ModInstallInstanceItem(instance, instance.InstanceName, instance.ShortDisplay))
            .ToArray();
        RefreshInstances();
    }

    public ModVersionFileItem File { get; }

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
    public bool HasNoInstances => Instances.Count == 0;
    public bool CanInstall => SelectedInstance is not null;
    [ObservableProperty] public partial bool ShowAllInstances { get; set; }
    [ObservableProperty] public partial ModInstallInstanceItem? SelectedInstance { get; set; }

    partial void OnSelectedInstanceChanged(ModInstallInstanceItem? value) => OnPropertyChanged(nameof(CanInstall));

    partial void OnShowAllInstancesChanged(bool value) => RefreshInstances();

    private void RefreshInstances()
    {
        var selectedPath = SelectedInstance?.Instance.InstanceFolderPath;
        var compatibleLoaders = File.GroupKeys.Select(key => key.Loader).Where(loader => loader != "通用").Distinct()
            .ToHashSet();
        var visibleInstances = ShowAllInstances || compatibleLoaders.Count == 0
            ? _allInstances
            : _allInstances.Where(item => item.Instance.MinecraftEntry is ModifiedMinecraftEntry entry &&
                entry.ModLoaders.Any(loader => compatibleLoaders.Contains(LoaderName(loader.Type)))).ToArray();

        Instances.Clear();
        foreach (var instance in visibleInstances) Instances.Add(instance);
        SelectedInstance = Instances.FirstOrDefault(item => item.Instance.InstanceFolderPath == selectedPath) ??
                           Instances.FirstOrDefault(item =>
                               item.Instance.InstanceFolderPath == Data.UiProperty.LastModInstallInstancePath) ??
                           Instances.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoInstances));
    }

    private static string LoaderName(MinecraftLaunch.Base.Enums.ModLoaderType loader) => loader switch
    {
        MinecraftLaunch.Base.Enums.ModLoaderType.NeoForge => "NeoForge",
        MinecraftLaunch.Base.Enums.ModLoaderType.Forge => "Forge",
        MinecraftLaunch.Base.Enums.ModLoaderType.Fabric => "Fabric",
        MinecraftLaunch.Base.Enums.ModLoaderType.Quilt => "Quilt",
        _ => string.Empty
    };

    public void Install()
    {
        if (SelectedInstance is not null)
            Data.UiProperty.LastModInstallInstancePath = SelectedInstance.Instance.InstanceFolderPath;
        RequestClose?.Invoke(this, new ModInstallDialogResult(ModDownloadDestination.Install, SelectedInstance?.Instance));
    }

    public void SaveAs() => RequestClose?.Invoke(this, new ModInstallDialogResult(ModDownloadDestination.SaveAs, null));
    public void Cancel() => RequestClose?.Invoke(this, null);
    public void Close() => Cancel();
    public event EventHandler<object?>? RequestClose;
}

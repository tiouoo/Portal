using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
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
    public ModInstallDialogViewModel(ModVersionFileItem file, IEnumerable<MinecraftInstance> instances)
    {
        File = file;
        Instances = instances.Where(instance => instance.IsJava)
            .Select(instance => new ModInstallInstanceItem(instance, instance.InstanceName, instance.ShortDisplay))
            .ToArray();
        SelectedInstance = Instances.FirstOrDefault();
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

    public IReadOnlyList<ModInstallInstanceItem> Instances { get; }
    public bool HasNoInstances => Instances.Count == 0;
    public bool CanInstall => SelectedInstance is not null;
    [ObservableProperty] public partial ModInstallInstanceItem? SelectedInstance { get; set; }

    partial void OnSelectedInstanceChanged(ModInstallInstanceItem? value) => OnPropertyChanged(nameof(CanInstall));

    public void Install() => RequestClose?.Invoke(this,
        new ModInstallDialogResult(ModDownloadDestination.Install, SelectedInstance?.Instance));

    public void SaveAs() => RequestClose?.Invoke(this, new ModInstallDialogResult(ModDownloadDestination.SaveAs, null));
    public void Cancel() => RequestClose?.Invoke(this, null);
    public void Close() => Cancel();
    public event EventHandler<object?>? RequestClose;
}
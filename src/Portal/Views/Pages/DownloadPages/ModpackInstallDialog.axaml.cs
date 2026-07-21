using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using TioUi.Common.Interfaces;

namespace Portal.Views.Pages.DownloadPages;

public partial class ModpackInstallDialog : UserControl
{
    public ModpackInstallDialog() => InitializeComponent();
    private void Install_Click(object? sender, RoutedEventArgs e) => (DataContext as ModpackInstallDialogViewModel)?.Install();
    private void SaveAs_Click(object? sender, RoutedEventArgs e) => (DataContext as ModpackInstallDialogViewModel)?.SaveAs();
    private void Cancel_Click(object? sender, RoutedEventArgs e) => (DataContext as ModpackInstallDialogViewModel)?.Cancel();
}

public enum ModpackDownloadDestination { Install, SaveAs }
public sealed record ModpackInstallDialogResult(ModpackDownloadDestination Destination, MinecraftFolderEntry? Folder,
    string? InstanceId);
public sealed record ModpackInstallFolderItem(MinecraftFolderEntry Folder, string Name, string Path);

public partial class ModpackInstallDialogViewModel : ObservableObject, IDialogContext
{
    public ObservableCollection<ModpackInstallFolderItem> MinecraftFolders { get; } = [];
    public bool HasNoMinecraftFolders => MinecraftFolders.Count == 0;
    public bool HasInvalidInstanceId => string.IsNullOrWhiteSpace(InstanceId) ||
                                        InstanceId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
    public bool InstanceIdExists => SelectedMinecraftFolder is not null && !HasInvalidInstanceId &&
                                    Directory.Exists(Path.Combine(SelectedMinecraftFolder.Folder.FolderPath, "versions", InstanceId.Trim()));
    public bool CanInstall => SelectedMinecraftFolder is not null && !HasInvalidInstanceId && !InstanceIdExists;
    [ObservableProperty] public partial ModpackInstallFolderItem? SelectedMinecraftFolder { get; set; }
    [ObservableProperty] public partial string InstanceId { get; set; }

    public ModpackInstallDialogViewModel(string suggestedInstanceId)
    {
        foreach (var folder in Data.ConfigEntry.MinecraftFolders.Where(folder => folder.SupportsTraditionalInstallation))
            MinecraftFolders.Add(new ModpackInstallFolderItem(folder, folder.FolderName, folder.FolderPath));
        SelectedMinecraftFolder = MinecraftFolders.FirstOrDefault(item => item.Folder == Data.ConfigEntry.DefaultMinecraftFolder)
                                  ?? MinecraftFolders.FirstOrDefault();
        InstanceId = suggestedInstanceId;
    }

    partial void OnSelectedMinecraftFolderChanged(ModpackInstallFolderItem? value) => UpdateInstanceIdState();
    partial void OnInstanceIdChanged(string value) => UpdateInstanceIdState();

    private void UpdateInstanceIdState()
    {
        OnPropertyChanged(nameof(HasInvalidInstanceId));
        OnPropertyChanged(nameof(InstanceIdExists));
        OnPropertyChanged(nameof(CanInstall));
    }

    public void Install() => RequestClose?.Invoke(this, new ModpackInstallDialogResult(ModpackDownloadDestination.Install,
        SelectedMinecraftFolder?.Folder, InstanceId.Trim()));
    public void SaveAs() => RequestClose?.Invoke(this, new ModpackInstallDialogResult(ModpackDownloadDestination.SaveAs,
        null, null));
    public void Cancel() => RequestClose?.Invoke(this, null);
    public void Close() => Cancel();
    public event EventHandler<object?>? RequestClose;
}

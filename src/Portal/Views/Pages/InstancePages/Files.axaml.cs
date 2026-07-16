using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Portal.Core.Minecraft.Classes;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Extensions;
using TioUi.Common.Extensions;

namespace Portal.Views.Pages.InstancePages;

public partial class Files : UserControl
{
    public MinecraftInstance Instance { get; }
    public ObservableCollection<InstanceFolderItem> Folders { get; } = [];

    public Files(MinecraftInstance instance)
    {
        Instance = instance;
        foreach (var (name, folder) in new[]
                 {
                     ("模组", MinecraftSpecialFolder.ModsFolder),
                     ("存档", MinecraftSpecialFolder.SavesFolder),
                     ("资源包", MinecraftSpecialFolder.ResourcePacksFolder),
                     ("光影包", MinecraftSpecialFolder.ShaderPacksFolder),
                     ("截图", MinecraftSpecialFolder.ScreenshotsFolder),
                 })
            Folders.Add(new InstanceFolderItem(name, Instance.GetSpecialFolder(folder)));

        InitializeComponent();
        DataContext = this;
    }

    public Files()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void OpenPath(string path)
    {
        _ = this.GetTopLevel().Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }

    private void MenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        var tag = (sender as Control)?.Tag as string;
        switch (tag)
        {
            case "root":
                OpenPath(Instance.FolderPath);
                break;
            case "version":
                OpenPath(Instance.MinecraftPath);
                break;
            case "mods":
                OpenPath(Instance.GetSpecialFolder(MinecraftSpecialFolder.ModsFolder));
                break;
            case "resource":
                OpenPath(Instance.GetSpecialFolder(MinecraftSpecialFolder.ResourcePacksFolder));
                break;
            case "shader":
                OpenPath(Instance.GetSpecialFolder(MinecraftSpecialFolder.ShaderPacksFolder));
                break;
            case "saves":
                OpenPath(Instance.GetSpecialFolder(MinecraftSpecialFolder.SavesFolder));
                break;
            case "screenshot":
                OpenPath(Instance.GetSpecialFolder(MinecraftSpecialFolder.ScreenshotsFolder));
                break;
            case "logs":
                OpenPath(Instance.GetSpecialFolder(MinecraftSpecialFolder.LogsFolder));
                break;
            case "config":
                OpenPath(Instance.GetSpecialFolder(MinecraftSpecialFolder.ConfigFolder));
                break;
        }
    }
}

public record InstanceFolderItem(string Name, string Path);
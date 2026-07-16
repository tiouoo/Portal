using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Portal.Core.Minecraft.Classes;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Extensions;
using TioUi.Common.Extensions;

namespace Portal.Views.Pages.InstancePages;

public partial class Properties : DataUserControl
{
    public MinecraftInstance Instance { get; }

    public Properties(MinecraftInstance instance)
    {
        Instance = instance;
        InitializeComponent();
        DataContext = this;
    }
    public Properties()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void Save_Click(object? sender, RoutedEventArgs e) => Instance.SaveConfig();

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control)
            _ = (sender as Control)!.GetTopLevel().Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(Instance.InstanceFolderPath));
    }
}

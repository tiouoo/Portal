using Avalonia.Controls;
using Avalonia.Interactivity;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Operations.OpenFile;
using Portal.ViewModels;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.SettingPages;

public partial class GameFolder : DataUserControl
{
    public GameFolder()
    {
        InitializeComponent();
        DataContext = this;
    }

    private async void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        var options = new OverlayDialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            CanResize = false,
            VerticalOffset = 110,
            VerticalAnchor = VerticalPosition.Top
        };

        var result = await OverlayDialog
            .ShowCustomAsync<NewMinecraftFolder, NewMinecraftFolderViewModel, MinecraftFolderEntry>(
                new NewMinecraftFolderViewModel(Data.ConfigEntry.MinecraftFolders.Select(x
                    => x.FolderPath).ToList()), hostId: (sender as Control)!.TryGetHostId(), options: options);

        if (result == null) return;
        Data.ConfigEntry.MinecraftFolders.Add(new MinecraftFolderEntry
        {
            FolderName = result.FolderName,
            FolderPath = result.FolderPath
        });
    }

    private void Button1_OnClick(object? sender, RoutedEventArgs e)
    {
        var folder = (sender as Control).Tag as MinecraftFolderEntry;
        Data.ConfigEntry.MinecraftFolders.Remove(folder!);
    }
}
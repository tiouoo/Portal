using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Operations.OpenFile;
using Portal.Module.AggregatedSearch;
using Portal.ViewModels;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;
using Tio.Avalonia.Standard.Tab.Gateway;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("游戏文件夹", "设置/游戏文件夹", "GameFolder")]
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
        Data.ConfigEntry.MinecraftFolders.Add(result);
    }

    private void Button1_OnClick(object? sender, RoutedEventArgs e)
    {
        var folder = (sender as Control).Tag as MinecraftFolderEntry;
        if (folder == null)
            return;
        var restoresDefaultFolder = folder.DetectedLayout.Kind == MinecraftFolderKind.Standard &&
                                    Data.ConfigEntry.TraditionalMinecraftFolders.Count() == 1;
        Data.ConfigEntry.MinecraftFolders.Remove(folder);
        if (restoresDefaultFolder)
        {
            Dispatcher.UIThread.Post(() => NotificationGateway.Notice(this.GetTopLevel(),
                "至少保留一个 .minecraft 传统文件夹",
                NotificationType.Warning));
        }
    }
}

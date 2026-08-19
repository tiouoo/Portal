using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Portal.Core.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Portal.ViewModels;
using Portal.Views.Components.Operations.OpenFile;
using Portal.Views.Pages.DownloadPages;
using Portal.Views.Widgets;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;
using NewMinecraftFolderViewModel = Portal.Views.Components.Operations.OpenFile.NewMinecraftFolderViewModel;

namespace Portal.Views.Pages;

public abstract partial class InstanceListPageBase : Dsc, ITioTabPage
{
    protected InstanceListPageBase()
    {
        AddHandler(InstanceCard.FavoriteChangedEvent, OnInstanceCardFavoriteChanged);
        AddHandler(RecentPlayCard.FavoriteChangedEvent, OnRecentPlayCardFavoriteChanged);
        AddHandler(RecentPlaysSection.RefreshRequestedEvent, OnRefreshRequested);
        AddHandler(InstanceListToolbar.RefreshRequestedEvent, OnRefreshRequested);
        AddHandler(InstanceListToolbar.OpenInstancesRequestedEvent, OnOpenInstancesRequested);
        AddHandler(InstanceListToolbar.ImportModpackRequestedEvent, OnImportModpackRequested);
        AddHandler(InstanceListToolbar.AddFolderRequestedEvent, OnAddFolderRequested);
        AddHandler(InstanceListToolbar.CreateInstanceRequestedEvent, OnCreateInstanceRequested);
    }

    protected abstract InstanceListViewModelBase PageViewModel { get; }

    public abstract PageInfo PageInfo { get; init; }

    public TabEntry HostTab { get; set; }

    public virtual void OnClose()
    {
        PageViewModel.Dispose();
        DataContext = null;
    }

    private void OnInstanceCardFavoriteChanged(object? sender, RoutedEventArgs e)
    {
        PageViewModel.ApplyFilterAndSort();
    }

    private void OnRecentPlayCardFavoriteChanged(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Control { DataContext: RecentPlayItem item } && PageViewModel is RecentPlaysViewModelBase viewModel)
            viewModel.ToggleRecentPlayFavorite(item);
    }

    private async void OnRefreshRequested(object? sender, RoutedEventArgs e)
    {
        await RefreshInstancesAndRecentPlaysAsync();
    }

    private void OnOpenInstancesRequested(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not TioTabWindowBase window)
            return;

        var tab = new TabEntry(window, new InstancesPage());
        window.CreateTab(tab);
        window.SelectTab(tab);
    }

    private async void OnImportModpackRequested(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var file = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = CommonLanguageManager.Instance.modpack_importTitle.CurrentValue(),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(CommonLanguageManager.Instance.modpack_fileType.CurrentValue())
            {
                Patterns = ["*.mrpack", "*.zip"]
            }]
        });
        var archivePath = file.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(archivePath))
            return;

        _ = ModpackInstallation.TryInstallFromPath(topLevel, archivePath);
    }

    private async void OnAddFolderRequested(object? sender, RoutedEventArgs e)
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
                    => x.FolderPath).ToList()), this.TryGetHostId(), options);

        if (result == null) return;
        Data.ConfigEntry.MinecraftFolders.Add(result);
    }

    private async void OnCreateInstanceRequested(object? sender, RoutedEventArgs e)
    {
        var options = new OverlayDialogOptions
        {
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            CanResize = false,
            IsCloseButtonVisible = false
        };
        await OverlayDialog.ShowCustomAsync<CreateInstanceDialog, CreateInstanceDialogViewModel, bool>(
            new CreateInstanceDialogViewModel(), this.TryGetHostId(), options);
    }

    protected virtual async Task RefreshInstancesAndRecentPlaysAsync()
    {
        InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
        PageViewModel.ApplyFilterAndSort();
        await RecentPlayListService.Instance.RefreshAsync();
    }

    protected void AddFolder_Click(object? sender, RoutedEventArgs e)
    {
        OnAddFolderRequested(sender, e);
    }

    protected void ToDownload_Click(object? sender, RoutedEventArgs e)
    {
        OnCreateInstanceRequested(sender, e);
    }
}

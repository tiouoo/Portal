using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Portal.Core.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Module.Widgets;
using Portal.Localization;
using Portal.ViewModels;
using Portal.Views.Components.Operations.OpenFile;
using Portal.Views.Pages.DownloadPages;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;
using NewMinecraftFolderViewModel = Portal.Views.Components.Operations.OpenFile.NewMinecraftFolderViewModel;

namespace Portal.Views.Widgets;

public partial class InstanceListWidget : IWidgetContent
{
    private InstanceListWidgetViewModel _viewModel;

    public InstanceListWidget() : this(new WidgetCellSize(6, 3))
    {
    }

    public InstanceListWidget(WidgetCellSize size)
    {
        Size = size;
        InitializeComponent();
        _viewModel = CreateViewModel();
        DataContext = _viewModel;
        EmptyText.Text = WidgetsLanguageManager.Instance.contextmenu_noInstances.CurrentValue();
        AddHandler(InstanceCard.FavoriteChangedEvent, OnFavoriteChanged);
        AddHandler(InstanceListToolbar.RefreshRequestedEvent, OnRefreshRequested);
        AddHandler(InstanceListToolbar.ImportModpackRequestedEvent, OnImportModpackRequested);
        AddHandler(InstanceListToolbar.AddFolderRequestedEvent, OnAddFolderRequested);
        AddHandler(InstanceListToolbar.CreateInstanceRequestedEvent, OnCreateInstanceRequested);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (_viewModel.IsDisposed)
        {
            _viewModel = CreateViewModel();
            DataContext = _viewModel;
        }
        _viewModel.ApplyFilterAndSort();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _viewModel?.Dispose();
    }

    private static InstanceListWidgetViewModel CreateViewModel()
    {
        var viewModel = new InstanceListWidgetViewModel();
        viewModel.SelectedSortOption = viewModel.SortOptions.FirstOrDefault(option =>
            option.SortType == Data.ConfigEntry.DefaultInstanceSortType);
        return viewModel;
    }

    private void OnFavoriteChanged(object? sender, RoutedEventArgs e) => _viewModel.ApplyFilterAndSort();

    private void OnRefreshRequested(object? sender, RoutedEventArgs e)
    {
        InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
        _viewModel.ApplyFilterAndSort();
    }

    private async void OnImportModpackRequested(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = CommonLanguageManager.Instance.modpack_importTitle.CurrentValue(),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(CommonLanguageManager.Instance.modpack_fileType.CurrentValue())
                {
                    Patterns = ["*.mrpack", "*.zip"]
                }
            ]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            _ = ModpackInstallation.TryInstallFromPath(topLevel, path);
    }

    private async void OnAddFolderRequested(object? sender, RoutedEventArgs e)
    {
        var result = await OverlayDialog
            .ShowCustomAsync<NewMinecraftFolder, NewMinecraftFolderViewModel, MinecraftFolderEntry>(
                new NewMinecraftFolderViewModel(Data.ConfigEntry.MinecraftFolders
                    .Select(folder => folder.FolderPath).ToList()), this.TryGetHostId(), new OverlayDialogOptions
                {
                    Mode = DialogMode.None,
                    Buttons = DialogButton.None,
                    CanLightDismiss = false,
                    CanDragMove = true,
                    IsCloseButtonVisible = false,
                    CanResize = false,
                    VerticalOffset = 110,
                    VerticalAnchor = VerticalPosition.Top
                });
        if (result != null)
            Data.ConfigEntry.MinecraftFolders.Add(result);
    }

    private async void OnCreateInstanceRequested(object? sender, RoutedEventArgs e)
    {
        await OverlayDialog.ShowCustomAsync<CreateInstanceDialog, CreateInstanceDialogViewModel, bool>(
            new CreateInstanceDialogViewModel(), this.TryGetHostId(), new OverlayDialogOptions
            {
                Buttons = DialogButton.None,
                CanLightDismiss = false,
                CanDragMove = true,
                CanResize = false,
                IsCloseButtonVisible = false
            });
    }
}

internal sealed class InstanceListWidgetViewModel : InstanceListViewModelBase
{
    public bool IsDisposed { get; private set; }

    public override void Dispose()
    {
        base.Dispose();
        IsDisposed = true;
    }
}

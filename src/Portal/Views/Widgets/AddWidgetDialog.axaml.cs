using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Classes.Entries;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Module.Widgets;
using Portal.Localization;
using Portal.Module;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Interfaces;
using TioUi.Controls;

namespace Portal.Views.Widgets;

public sealed partial class AddWidgetDialogViewModel : ObservableObject, IDialogContext
{
    private readonly string? _hostId;
    private readonly WidgetWorkspace _workspace;

    [ObservableProperty] private string _searchText = string.Empty;

    public AddWidgetDialogViewModel(WidgetWorkspace workspace, string? hostId)
    {
        _workspace = workspace;
        _hostId = hostId;
        ApplyFilter();
    }

    public WidgetCategory SelectedCategory { get; set; } = WidgetCategory.Game;

    public ObservableCollection<WidgetDefinition> Items { get; } = [];

    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    [RelayCommand]
    private void SelectCategory(WidgetCategory category)
    {
        SelectedCategory = category;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var keyword = SearchText?.Trim();
        var list = WidgetRegistry.Definitions
            .Where(d => d.Category == SelectedCategory)
            .Where(d => string.IsNullOrEmpty(keyword) ||
                        d.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        d.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Items.Clear();
        foreach (var definition in list)
            Items.Add(definition);
    }

    public async Task AddWidgetAsync(WidgetDefinition definition)
    {
        WidgetLayoutData? template = null;

        switch (definition.Kind)
        {
            case WidgetKind.ServerList:
            case WidgetKind.InstanceList:
            case WidgetKind.WorldList:
            case WidgetKind.RecentPlayList:
            case WidgetKind.RecentInstanceList:
                template = new WidgetLayoutData { Data = new GameListWidgetData() };
                break;
            case WidgetKind.Instance:
            {
                var instance = await PickInstanceAsync();
                if (instance == null) return;
                template = new WidgetLayoutData
                {
                    Data = new InstanceWidgetData { InstanceFolderPath = instance.InstanceFolderPath }
                };
                break;
            }
            case WidgetKind.QuickWorld:
            {
                var instance = await PickInstanceAsync();
                if (instance == null) return;
                var world = await PickWorldAsync(instance);
                if (world == null) return;
                template = new WidgetLayoutData
                {
                    Data = new QuickWorldWidgetData
                    {
                        InstanceFolderPath = instance.InstanceFolderPath,
                        WorldFolderName = world.FolderName
                    }
                };
                break;
            }
            case WidgetKind.QuickServer:
            {
                var instance = await PickInstanceAsync();
                if (instance == null) return;
                var server = await PickServerAsync(instance);
                if (server == null) return;
                template = new WidgetLayoutData
                {
                    Data = new QuickServerWidgetData
                    {
                        InstanceFolderPath = instance.InstanceFolderPath,
                        ServerAddress = server.Address,
                        ServerPort = server.Port
                    }
                };
                break;
            }
            case WidgetKind.Image:
            {
                var path = await PickImageAsync();
                if (string.IsNullOrEmpty(path)) return;
                template = new WidgetLayoutData
                {
                    Data = new ImageWidgetData { ImagePath = path }
                };
                break;
            }
        }

        var host = _workspace.AddWidget(definition.Kind, template);
        if (host != null)
        {
            var topLevel = TopLevel.GetTopLevel(_workspace);
            topLevel?.Notice(string.Format(CommonLanguageManager.Instance.widgets_added.CurrentValue(),
                definition.Name), NotificationType.Success);
            RequestClose?.Invoke(this, true);
        }
    }

    private async Task<MinecraftInstance?> PickInstanceAsync()
    {
        var options = new OverlayDialogOptions
        {
            Buttons = DialogButton.None,
            CanLightDismiss = true,
            CanDragMove = true,
            CanResize = true,
            IsCloseButtonVisible = true
        };

        var result = await OverlayDialog
            .ShowCustomAsync<InstancePickerDialog, InstancePickerDialogViewModel, object?>(
                new InstancePickerDialogViewModel(), _hostId, options);

        return result as MinecraftInstance;
    }

    private async Task<WorldPickItem?> PickWorldAsync(MinecraftInstance instance)
    {
        var options = new OverlayDialogOptions
        {
            Buttons = DialogButton.None,
            CanLightDismiss = true,
            CanDragMove = true,
            CanResize = true,
            IsCloseButtonVisible = true
        };

        var result = await OverlayDialog
            .ShowCustomAsync<WorldPickerDialog, WorldPickerDialogViewModel, object?>(
                new WorldPickerDialogViewModel(instance), _hostId, options);

        return result as WorldPickItem;
    }

    private async Task<ServerConnectResult?> PickServerAsync(MinecraftInstance instance)
    {
        var options = new OverlayDialogOptions
        {
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            CanResize = false,
            IsCloseButtonVisible = true
        };

        var result = await OverlayDialog
            .ShowCustomAsync<ServerConnectDialog, ServerConnectDialogViewModel, object?>(
                new ServerConnectDialogViewModel(instance), _hostId, options);

        return result as ServerConnectResult;
    }

    private async Task<string?> PickImageAsync()
    {
        var topLevel = TopLevel.GetTopLevel(_workspace);
        if (topLevel == null)
            return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = CommonLanguageManager.Instance.widgets_selectImage.CurrentValue(),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(CommonLanguageManager.Instance.saves_image.CurrentValue())
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp", "*.ico"]
                }
            ]
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }
}

public partial class AddWidgetDialog : UserControl
{
    public AddWidgetDialog()
    {
        InitializeComponent();
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AddWidgetDialogViewModel vm)
            vm.Close();
    }

    private async void Add_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is WidgetDefinition definition &&
            DataContext is AddWidgetDialogViewModel viewModel)
            await viewModel.AddWidgetAsync(definition);
    }
}

using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Classes.Entries;
using Portal.Core.Minecraft.Classes;
using Portal.Module.Widgets;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Common.Interfaces;
using TioUi.Controls;

namespace Portal.Views.Widgets;

public sealed partial class AddWidgetDialogViewModel : ObservableObject, IDialogContext
{
    private readonly WidgetWorkspace _workspace;
    private readonly string? _hostId;

    [ObservableProperty] private string _searchText = string.Empty;

    public ObservableCollection<WidgetDefinition> Items { get; } = [];

    public AddWidgetDialogViewModel(WidgetWorkspace workspace, string? hostId)
    {
        _workspace = workspace;
        _hostId = hostId;
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var keyword = SearchText?.Trim();
        var list = WidgetRegistry.Definitions
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
            case WidgetKind.Instance:
            {
                var instance = await PickInstanceAsync();
                if (instance == null) return;
                template = new WidgetLayoutData { InstanceFolderPath = instance.InstanceFolderPath };
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
                    InstanceFolderPath = instance.InstanceFolderPath,
                    WorldFolderName = world.FolderName
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
                    InstanceFolderPath = instance.InstanceFolderPath,
                    ServerAddress = server.Address,
                    ServerPort = server.Port
                };
                break;
            }
        }

        var host = _workspace.AddWidget(definition.Kind, template);
        if (host != null)
        {
            var topLevel = TopLevel.GetTopLevel(_workspace);
            topLevel?.Notice($"已添加 {definition.Name}", NotificationType.Success);
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
                new InstancePickerDialogViewModel(), hostId: _hostId, options: options);

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
                new WorldPickerDialogViewModel(instance), hostId: _hostId, options: options);

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
                new ServerConnectDialogViewModel(instance), hostId: _hostId, options: options);

        return result as ServerConnectResult;
    }

    public void Close() => RequestClose?.Invoke(this, null);
    public event EventHandler<object?>? RequestClose;
}

public partial class AddWidgetDialog : UserControl
{
    public AddWidgetDialog()
    {
        InitializeComponent();
    }

    private async void Add_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is WidgetDefinition definition &&
            DataContext is AddWidgetDialogViewModel viewModel)
            await viewModel.AddWidgetAsync(definition);
    }
}

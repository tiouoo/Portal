using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Portal.Core.Classes.Entries;
using Portal.Core.Const;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Services;
using Portal.Core.Module.Widgets;
using Portal.Localization;
using Portal.Module;
using Portal.Views.Pages;
using Portal.Views.Pages.InstancePages;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Widgets;

public partial class GameListWidget : IWidgetContent, IWidgetContextMenuProvider, IWidgetPersistenceAware
{
    private WidgetLayoutData? _layout;
    private Action? _saveLayout;
    private int _refreshVersion;

    private readonly ContextMenu _itemContextMenu = new()
    {
        Cursor = new Cursor(StandardCursorType.Arrow)
    };

    public GameListWidget() : this(new WidgetCellSize(2, 2))
    {
    }

    public GameListWidget(WidgetCellSize size)
    {
        Size = size;
        InitializeComponent();
        DataContext = this;
    }

    public ObservableCollection<GameListItem> Items { get; } = [];

    public string Title => Kind switch
    {
        WidgetKind.ServerList => CommonLanguageManager.Instance.widgets_serverList.CurrentValue(),
        WidgetKind.InstanceList => CommonLanguageManager.Instance.widgets_instanceList.CurrentValue(),
        WidgetKind.WorldList => CommonLanguageManager.Instance.widgets_worldList.CurrentValue(),
        WidgetKind.RecentPlayList => CommonLanguageManager.Instance.widgets_recentPlayList.CurrentValue(),
        WidgetKind.RecentInstanceList => CommonLanguageManager.Instance.widgets_recentInstanceList.CurrentValue(),
        _ => string.Empty
    };

    public string CountText => Items.Count.ToString();
    public bool IsEmpty => Items.Count == 0;

    public string EmptyText => IsRecent
        ? WidgetsLanguageManager.Instance.contextmenu_noRecentItems.CurrentValue()
        : WidgetsLanguageManager.Instance.contextmenu_emptyList.CurrentValue();

    private bool IsRecent => Kind is WidgetKind.RecentPlayList or WidgetKind.RecentInstanceList;
    private GameListWidgetData Data => (GameListWidgetData)_layout!.Data!;

    public override void Initialize(WidgetLayoutData layout)
    {
        _layout = layout;
        layout.Data ??= new GameListWidgetData();
        if (layout.Data is not GameListWidgetData)
            layout.Data = new GameListWidgetData();
        if (this.FindControl<TextBlock>("TitleTextBlock") is { } title)
            title.Text = Title;

        InstanceManager.Instance.InstancesChanged += OnSourceChanged;
        InstanceManager.Instance.InstanceIconChanged += OnInstanceIconChanged;
        RecentPlayListService.Instance.Refreshed += OnSourceChanged;
        Unloaded += OnUnloaded;
        _ = RefreshAsync();
    }

    public IReadOnlyList<MenuItem> CreateContextMenuItems(Action saveLayout)
    {
        _saveLayout = saveLayout;
        if (!IsRecent)
        {
            var addItem = new MenuItem
            {
                Header = WidgetsLanguageManager.Instance.contextmenu_addItem.CurrentValue(),
                Icon = IconResources.CreateIcon("\ue645", 18)
            };
            addItem.Click += (_, _) => _ = AddItemAsync();
            return [addItem];
        }

        var limitMenu = new MenuItem
        {
            Header = WidgetsLanguageManager.Instance.contextmenu_displayLimit.CurrentValue(),
            Icon = IconResources.CreateIcon("\ue63c", 18)
        };
        foreach (var limit in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 })
        {
            var item = new MenuItem
            {
                Header = limit.ToString(),
                IsChecked = Data.Limit == limit,
                Classes = { "hide-icon" }
            };
            item.Click += (_, _) =>
            {
                Data.Limit = limit;
                saveLayout();
                _ = RefreshAsync();
            };
            limitMenu.Items.Add(item);
        }

        return [limitMenu];
    }

    public void SetSaveLayoutAction(Action saveLayout)
    {
        _saveLayout = saveLayout;
    }

    private void OnSourceChanged(object? sender, EventArgs e) => _ = RefreshAsync();

    private void OnInstanceIconChanged(object? sender, MinecraftInstance instance) => _ = RefreshAsync();

    private void OnUnloaded(object? sender, EventArgs e)
    {
        InstanceManager.Instance.InstancesChanged -= OnSourceChanged;
        InstanceManager.Instance.InstanceIconChanged -= OnInstanceIconChanged;
        RecentPlayListService.Instance.Refreshed -= OnSourceChanged;
        Unloaded -= OnUnloaded;
    }

    private async Task RefreshAsync()
    {
        var version = ++_refreshVersion;
        IReadOnlyList<GameListItem> items = Kind switch
        {
            WidgetKind.InstanceList => CreateInstanceItems(Data.Items),
            WidgetKind.ServerList => CreateServerItems(Data.Items),
            WidgetKind.WorldList => await CreateWorldItemsAsync(Data.Items),
            WidgetKind.RecentPlayList => CreateRecentPlayItems(),
            WidgetKind.RecentInstanceList => CreateRecentInstanceItems(),
            _ => []
        };
        if (version != _refreshVersion)
            return;

        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        RaiseDisplayPropertiesChanged();
    }

    private IReadOnlyList<GameListItem> CreateInstanceItems(IEnumerable<GameListWidgetEntry> entries)
    {
        return entries.Select((entry, index) =>
            {
                var instance = ResolveInstance(entry.InstanceFolderPath);
                return instance == null ? null : GameListItem.ForInstance(instance, entry, index);
            })
            .Where(item => item != null).Cast<GameListItem>().ToArray();
    }

    private IReadOnlyList<GameListItem> CreateServerItems(IEnumerable<GameListWidgetEntry> entries)
    {
        return entries.Select((entry, index) =>
            {
                var instance = ResolveInstance(entry.InstanceFolderPath);
                if (instance == null || string.IsNullOrWhiteSpace(entry.ServerAddress)) return null;
                var port = entry.ServerPort ?? 25565;
                var name = entry.DisplayName ?? ServerPing.BuildDisplayAddress(entry.ServerAddress, port);
                return GameListItem.ForTarget(instance, entry, index, RecentPlayTargetType.Server,
                    entry.TargetId ?? $"server:{entry.ServerAddress}:{port}", name,
                    $"{ServerPing.BuildDisplayAddress(entry.ServerAddress, port)} · {instance.ShortDisplay}",
                    true, serverAddress: entry.ServerAddress, serverPort: port);
            })
            .Where(item => item != null).Cast<GameListItem>().ToArray();
    }

    private async Task<IReadOnlyList<GameListItem>> CreateWorldItemsAsync(IEnumerable<GameListWidgetEntry> entries)
    {
        var result = new List<GameListItem>();
        foreach (var (entry, index) in entries.Select((entry, index) => (entry, index)))
        {
            var instance = ResolveInstance(entry.InstanceFolderPath);
            if (instance == null || string.IsNullOrWhiteSpace(entry.TargetId)) continue;
            var world = (await new WorldSaveService().ScanAsync(instance))
                .FirstOrDefault(item => item.FolderName == entry.TargetId);
            if (world == null) continue;
            result.Add(GameListItem.ForTarget(instance, entry, index, RecentPlayTargetType.World,
                world.FolderName, string.IsNullOrWhiteSpace(world.LevelName) ? world.FolderName : world.LevelName,
                $"{world.Version ?? "?"} · {instance.ShortDisplay}", CanQuickEnterWorld(instance), world));
        }

        return result;
    }

    private IReadOnlyList<GameListItem> CreateRecentPlayItems()
    {
        return RecentPlayListService.Instance.Items
            .OrderByDescending(item => item.LastPlayedTime)
            .Take(Math.Clamp(Data.Limit, 1, 20))
            .Select((target, index) => GameListItem.ForRecentTarget(target, index))
            .ToArray();
    }

    private IReadOnlyList<GameListItem> CreateRecentInstanceItems()
    {
        return InstanceManager.Instance.Instances
            .Where(instance => instance.LastPlayTime != DateTime.MinValue)
            .OrderByDescending(instance => instance.LastPlayTime)
            .Take(Math.Clamp(Data.Limit, 1, 20))
            .Select((instance, index) => GameListItem.ForInstance(instance, null, index))
            .ToArray();
    }

    private void RaiseDisplayPropertiesChanged()
    {
        if (this.FindControl<TextBlock>("CountTextBlock") is { } count)
            count.Text = CountText;
        if (this.FindControl<TextBlock>("EmptyTextBlock") is { } empty)
        {
            empty.IsVisible = IsEmpty;
            empty.Text = EmptyText;
        }
    }

    private async Task AddItemAsync()
    {
        var instance = await PickInstanceAsync();
        if (instance == null) return;

        var entry = new GameListWidgetEntry { InstanceFolderPath = instance.InstanceFolderPath };
        if (Kind == WidgetKind.WorldList)
        {
            var world = await PickWorldAsync(instance);
            if (world == null) return;
            entry.TargetId = world.FolderName;
            entry.DisplayName = world.DisplayName;
        }
        else if (Kind == WidgetKind.ServerList)
        {
            var server = await PickServerAsync(instance);
            if (server == null) return;
            entry.ServerAddress = server.Address;
            entry.ServerPort = server.Port;
            entry.TargetId = $"server:{server.Address}:{server.Port}";
            entry.DisplayName = ServerPing.BuildDisplayAddress(server.Address, server.Port);
        }

        var duplicate = Data.Items.Any(item =>
            string.Equals(item.InstanceFolderPath, entry.InstanceFolderPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.TargetId, entry.TargetId, StringComparison.OrdinalIgnoreCase));
        if (!duplicate)
        {
            Data.Items.Add(entry);
            _saveLayout?.Invoke();
            await RefreshAsync();
        }
    }

    private async Task<MinecraftInstance?> PickInstanceAsync()
    {
        var result = await OverlayDialog.ShowCustomAsync<InstancePickerDialog, InstancePickerDialogViewModel, object?>(
            new InstancePickerDialogViewModel(), this.TryGetHostId(), CreatePickerOptions());
        return result as MinecraftInstance;
    }

    private async Task<WorldPickItem?> PickWorldAsync(MinecraftInstance instance)
    {
        var result = await OverlayDialog.ShowCustomAsync<WorldPickerDialog, WorldPickerDialogViewModel, object?>(
            new WorldPickerDialogViewModel(instance), this.TryGetHostId(), CreatePickerOptions());
        return result as WorldPickItem;
    }

    private async Task<ServerConnectResult?> PickServerAsync(MinecraftInstance instance)
    {
        var result = await OverlayDialog.ShowCustomAsync<ServerConnectDialog, ServerConnectDialogViewModel, object?>(
            new ServerConnectDialogViewModel(instance), this.TryGetHostId(), new OverlayDialogOptions
            {
                Buttons = DialogButton.None, CanLightDismiss = false, CanDragMove = true,
                CanResize = false, IsCloseButtonVisible = true
            });
        return result as ServerConnectResult;
    }

    private static OverlayDialogOptions CreatePickerOptions() => new()
    {
        Buttons = DialogButton.None, CanLightDismiss = true, CanDragMove = true,
        CanResize = true, IsCloseButtonVisible = true
    };

    private void Item_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed ||
            sender is not Control { Tag: GameListItem item } control)
            return;
        PopulateItemContextMenu(item);
        _itemContextMenu.Open(control);
        e.Handled = true;
    }

    private void PlayButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: GameListItem item })
            Launch(item);
        e.Handled = true;
    }

    private void PopulateItemContextMenu(GameListItem item)
    {
        _itemContextMenu.Close();
        _itemContextMenu.Items.Clear();
        if (item.CanPlay)
        {
            var play = CreateMenuItem(item.PlayText, "\ue613");
            play.Click += (_, _) => Launch(item);
            _itemContextMenu.Items.Add(play);
        }

        var details = CreateMenuItem(WidgetsLanguageManager.Instance.contextmenu_viewDetails.CurrentValue(), "\ue60c");
        details.Click += (_, _) => _ = ShowDetailsAsync(item);
        _itemContextMenu.Items.Add(details);

        if (!IsRecent)
        {
            _itemContextMenu.Items.Add(new Separator { Cursor = new Cursor(StandardCursorType.Arrow) });
            var remove = CreateMenuItem(WidgetsLanguageManager.Instance.contextmenu_removeFromList.CurrentValue(),
                "\ue640");
            remove.Click += (_, _) => Remove(item);
            _itemContextMenu.Items.Add(remove);
            _itemContextMenu.Items.Add(new Separator { Cursor = new Cursor(StandardCursorType.Arrow) });
            var up = CreateMenuItem(WidgetsLanguageManager.Instance.contextmenu_moveUp.CurrentValue(), "\ue61e");
            up.Click += (_, _) => Move(item.Index, -1);
            _itemContextMenu.Items.Add(up);
            var down = CreateMenuItem(WidgetsLanguageManager.Instance.contextmenu_moveDown.CurrentValue(), "\ue625");
            down.Click += (_, _) => Move(item.Index, 1);
            _itemContextMenu.Items.Add(down);
        }
    }

    private static MenuItem CreateMenuItem(string header, string icon)
    {
        return new MenuItem
        {
            Header = header,
            Icon = IconResources.CreateIcon(icon, 18),
            Cursor = new Cursor(StandardCursorType.Arrow)
        };
    }

    private void Launch(GameListItem item)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;
        RecentPlayTarget? target = null;
        if (item.TargetType != null)
            target = new RecentPlayTarget(item.Instance, item.TargetType.Value, item.TargetId!, item.Name, item.Details,
                item.LastPlayedTime, item.WorldInfo?.IconPath, ServerAddress: item.ServerAddress,
                ServerPort: item.ServerPort);
        _ = MinecraftLaunchService.LaunchAsync(item.Instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(item.Instance, log => MinecraftLogPage.Open(log, topLevel)), target);
    }

    private async Task ShowDetailsAsync(GameListItem item)
    {
        if (item.TargetType != RecentPlayTargetType.World)
        {
            if (TopLevel.GetTopLevel(this) is { } topLevel)
                InstanceDetailPage.Open(item.Instance, topLevel);
            return;
        }

        var info = item.WorldInfo ?? (await new WorldSaveService().ScanAsync(item.Instance))
            .FirstOrDefault(world => world.FolderName == item.TargetId);
        if (info == null) return;
        await OverlayDialog.ShowCustomAsync<WorldSaveDetails, WorldSaveDetailsViewModel, object?>(
            new WorldSaveDetailsViewModel(info, item.Instance), this.TryGetHostId(), CreatePickerOptions());
    }

    private void Remove(GameListItem item)
    {
        if (item.Index < 0 || item.Index >= Data.Items.Count) return;
        Data.Items.RemoveAt(item.Index);
        _saveLayout?.Invoke();
        _ = RefreshAsync();
    }

    private void Move(int index, int direction)
    {
        var target = index + direction;
        if (index < 0 || target < 0 || index >= Data.Items.Count || target >= Data.Items.Count) return;
        var entry = Data.Items[index];
        Data.Items.RemoveAt(index);
        Data.Items.Insert(target, entry);
        _saveLayout?.Invoke();
        _ = RefreshAsync();
    }

    private static MinecraftInstance? ResolveInstance(string path) =>
        InstanceManager.Instance.Instances.FirstOrDefault(instance =>
            string.Equals(instance.InstanceFolderPath, path, StringComparison.OrdinalIgnoreCase));

    private static bool CanQuickEnterWorld(MinecraftInstance instance) =>
        instance.MinecraftEntry is { } entry && entry.ReleaseTime > new DateTime(2023, 4, 4);
}

public sealed class GameListItem
{
    public required MinecraftInstance Instance { get; init; }
    public required string Name { get; init; }
    public required string Details { get; init; }
    public required int Index { get; init; }
    public required bool CanPlay { get; init; }
    public GameListWidgetEntry? Entry { get; init; }
    public RecentPlayTargetType? TargetType { get; init; }
    public string? TargetId { get; init; }
    public string? ServerAddress { get; init; }
    public int? ServerPort { get; init; }
    public WorldSaveInfo? WorldInfo { get; init; }
    public DateTime LastPlayedTime { get; init; }
    public IImage? ExplicitIcon { get; init; }

    public IImage? Icon => ExplicitIcon ?? (TargetType == RecentPlayTargetType.World &&
                                            WorldInfo?.IconPath is { } path && File.Exists(path)
        ? new Bitmap(path)
        : Instance[40]);

    public string PlayText => TargetType switch
    {
        RecentPlayTargetType.Server => WidgetsLanguageManager.Instance.contextmenu_enterServer.CurrentValue(),
        RecentPlayTargetType.World => WidgetsLanguageManager.Instance.contextmenu_enterWorld.CurrentValue(),
        _ => WidgetsLanguageManager.Instance.contextmenu_play.CurrentValue()
    };

    public string StartGameText => WidgetsLanguageManager.Instance.contextmenu_play.CurrentValue();

    public static GameListItem ForInstance(MinecraftInstance instance, GameListWidgetEntry? entry, int index) => new()
    {
        Instance = instance, Entry = entry, Index = index, Name = instance.InstanceName,
        Details = $"{instance.ShortDisplay} · {instance.DisplayLastPlayTime}", CanPlay = true,
        LastPlayedTime = instance.LastPlayTime
    };

    public static GameListItem ForTarget(MinecraftInstance instance, GameListWidgetEntry? entry, int index,
        RecentPlayTargetType type, string id, string name, string details, bool canPlay,
        WorldSaveInfo? worldInfo = null, string? serverAddress = null, int? serverPort = null) => new()
    {
        Instance = instance, Entry = entry, Index = index, TargetType = type, TargetId = id, Name = name,
        Details = details, CanPlay = canPlay, WorldInfo = worldInfo, ServerAddress = serverAddress,
        ServerPort = serverPort, LastPlayedTime = worldInfo?.LastPlayedTime ?? DateTime.MinValue
    };

    public static GameListItem ForRecentTarget(RecentPlayTarget target, int index) => new()
    {
        Instance = target.Instance, Index = index, TargetType = target.Type, TargetId = target.Id,
        Name = target.Name, Details = $"{target.Details} · {target.Instance.ShortDisplay}",
        CanPlay = target.CanQuickPlay, ServerAddress = target.ServerAddress, ServerPort = target.ServerPort,
        LastPlayedTime = target.LastPlayedTime,
        ExplicitIcon = CreateRecentIcon(target),
        WorldInfo = null
    };

    private static IImage? CreateRecentIcon(RecentPlayTarget target)
    {
        if (!string.IsNullOrEmpty(target.WorldIconPath) && File.Exists(target.WorldIconPath))
            return new Bitmap(target.WorldIconPath);
        if (target.ServerIconData is not { Length: > 0 })
            return null;
        using var stream = new MemoryStream(target.ServerIconData);
        return new Bitmap(stream);
    }
}
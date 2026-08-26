using System.ComponentModel;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Classes.Entries;
using Portal.Core.Const;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Services;
using Portal.Core.Module.AggregatedSearch;
using Portal.Core.Module.Initialize;
using Portal.Localization;
using Portal.Module;
using Portal.Module.DefaultPage;
using Portal.Views.Pages.InstancePages;
using Portal.Views.Widgets;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages;

[AggregatedSearchPage("pages_home", "pages_homePath", "Home")]
[DefaultPage("pages_home")]
public partial class HomePage : UserControl, ITioTabPage
{
    private readonly ContextMenu _itemContextMenu = new()
    {
        Cursor = new Cursor(StandardCursorType.Arrow)
    };
    private readonly HomePageViewModel _viewModel;

    public HomePage()
    {
        InitializeComponent();
        _viewModel = new HomePageViewModel(this);
        DataContext = _viewModel;
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = CommonLanguageManager.Instance.homePage_pageTitle.CurrentValue(),
        IconGlyph = "\ue619",
        IconFont = IconResources.FontFamilyName
    };

    public TabEntry HostTab { get; set; }

    public void OnClose()
    {
        _viewModel.Dispose();
        DataContext = null;
    }

    private void AddInstance_OnClick(object? sender, RoutedEventArgs e) =>
        _ = _viewModel.AddAsync(HomePageItemKind.Instance);

    private void AddWorld_OnClick(object? sender, RoutedEventArgs e) =>
        _ = _viewModel.AddAsync(HomePageItemKind.World);

    private void AddServer_OnClick(object? sender, RoutedEventArgs e) =>
        _ = _viewModel.AddAsync(HomePageItemKind.Server);

    private void PlayButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: HomePageGameItem item })
            _viewModel.Launch(item);
        e.Handled = true;
    }

    private void Item_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { Tag: HomePageGameItem item } control)
            return;

        e.Handled = true;

        _itemContextMenu.Close();
        _itemContextMenu.Items.Clear();
        if (item.CanPlay)
        {
            var play = CreateMenuItem(item.PlayText, "\ue613");
            play.Click += (_, _) => _viewModel.Launch(item);
            _itemContextMenu.Items.Add(play);
        }

        var details = CreateMenuItem(WidgetsLanguageManager.Instance.contextmenu_viewDetails.CurrentValue(), "\ue60c");
        details.Click += (_, _) => _ = _viewModel.ShowDetailsAsync(item);
        _itemContextMenu.Items.Add(details);
        _itemContextMenu.Items.Add(new Separator { Cursor = new Cursor(StandardCursorType.Arrow) });

        var remove = CreateMenuItem(WidgetsLanguageManager.Instance.contextmenu_removeFromList.CurrentValue(),
            "\ue640");
        remove.Click += (_, _) => _viewModel.Remove(item);
        _itemContextMenu.Items.Add(remove);
        _itemContextMenu.Items.Add(new Separator { Cursor = new Cursor(StandardCursorType.Arrow) });

        var up = CreateMenuItem(WidgetsLanguageManager.Instance.contextmenu_moveUp.CurrentValue(), "\ue61e");
        up.IsEnabled = item.Index > 0;
        up.Click += (_, _) => _viewModel.Move(item.Index, -1);
        _itemContextMenu.Items.Add(up);
        var down = CreateMenuItem(WidgetsLanguageManager.Instance.contextmenu_moveDown.CurrentValue(), "\ue625");
        down.IsEnabled = item.Index < Data.ConfigEntry.HomePageItems.Count - 1;
        down.Click += (_, _) => _viewModel.Move(item.Index, 1);
        _itemContextMenu.Items.Add(down);

        _itemContextMenu.Open(control);
    }

    private static MenuItem CreateMenuItem(string header, string icon) => new()
    {
        Header = header,
        Icon = IconResources.CreateIcon(icon, 18),
        Cursor = new Cursor(StandardCursorType.Arrow)
    };
}

public partial class HomePageViewModel : ObservableObject, IDisposable
{
    private readonly HomePage _view;
    private readonly DispatcherTimer _greetingTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private MinecraftAccount? _javaAccount;
    private BedrockAccount? _bedrockAccount;
    private bool _isDisposed;

    private int _refreshVersion;

    public HomePageViewModel(HomePage view)
    {
        _view = view;
        Data.ConfigEntry.PropertyChanged += ConfigEntry_OnPropertyChanged;
        InstanceManager.Instance.InstancesChanged += Source_OnChanged;
        InstanceManager.Instance.InstanceIconChanged += InstanceManager_OnInstanceIconChanged;
        _greetingTimer.Tick += GreetingTimer_OnTick;
        _greetingTimer.Start();
        RefreshAccountSubscriptions();
        RefreshGreeting();
        _ = RefreshItemsAsync();
    }

    public ObservableCollection<HomePageGameItem> Items { get; } = [];

    [ObservableProperty] public partial bool IsEmpty { get; private set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGreeting))]
    public partial string? Greeting { get; private set; }

    public bool HasGreeting => !string.IsNullOrWhiteSpace(Greeting);

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _greetingTimer.Stop();
        _greetingTimer.Tick -= GreetingTimer_OnTick;
        Data.ConfigEntry.PropertyChanged -= ConfigEntry_OnPropertyChanged;
        InstanceManager.Instance.InstancesChanged -= Source_OnChanged;
        InstanceManager.Instance.InstanceIconChanged -= InstanceManager_OnInstanceIconChanged;
        SetAccountSubscriptions(null, null);
    }

    public async Task AddAsync(HomePageItemKind kind)
    {
        var instance = await PickInstanceAsync();
        if (instance == null) return;

        var entry = new GameListWidgetEntry
        {
            ItemKind = kind,
            InstanceFolderPath = instance.InstanceFolderPath
        };
        switch (kind)
        {
            case HomePageItemKind.World:
                var world = await PickWorldAsync(instance);
                if (world == null) return;
                entry.TargetId = world.FolderName;
                entry.DisplayName = world.DisplayName;
                break;
            case HomePageItemKind.Server:
                var server = await PickServerAsync(instance);
                if (server == null) return;
                entry.ServerAddress = server.Address;
                entry.ServerPort = server.Port;
                entry.TargetId = $"server:{server.Address}:{server.Port}";
                entry.DisplayName = ServerPing.BuildDisplayAddress(server.Address, server.Port);
                break;
        }

        var duplicate = Data.ConfigEntry.HomePageItems.Any(item =>
            item.ItemKind == entry.ItemKind &&
            string.Equals(item.InstanceFolderPath, entry.InstanceFolderPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.TargetId, entry.TargetId, StringComparison.OrdinalIgnoreCase));
        if (duplicate) return;

        Data.ConfigEntry.HomePageItems.Add(entry);
        ConfigSaver.SaveConfig();
        await RefreshItemsAsync();
    }

    public void Launch(HomePageGameItem item)
    {
        if (TopLevel.GetTopLevel(_view) is not { } topLevel) return;
        RecentPlayTarget? target = null;
        if (item.TargetType != null)
            target = new RecentPlayTarget(item.Instance, item.TargetType.Value, item.TargetId!, item.Name, item.Details,
                item.LastPlayedTime, item.WorldInfo?.IconPath, ServerAddress: item.ServerAddress,
                ServerPort: item.ServerPort);
        _ = MinecraftLaunchService.LaunchAsync(item.Instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(item.Instance, log => MinecraftLogPage.Open(log, topLevel)), target);
    }

    public async Task ShowDetailsAsync(HomePageGameItem item)
    {
        if (item.TargetType != RecentPlayTargetType.World)
        {
            if (TopLevel.GetTopLevel(_view) is { } topLevel)
                InstanceDetailPage.Open(item.Instance, topLevel);
            return;
        }

        var info = item.WorldInfo ?? (await new WorldSaveService().ScanAsync(item.Instance))
            .FirstOrDefault(world => world.FolderName == item.TargetId);
        if (info == null) return;
        await OverlayDialog.ShowCustomAsync<WorldSaveDetails, WorldSaveDetailsViewModel, object?>(
            new WorldSaveDetailsViewModel(info, item.Instance), _view.TryGetHostId(), CreatePickerOptions());
    }

    public void Remove(HomePageGameItem item)
    {
        if (item.Index < 0 || item.Index >= Data.ConfigEntry.HomePageItems.Count) return;
        Data.ConfigEntry.HomePageItems.RemoveAt(item.Index);
        ConfigSaver.SaveConfig();
        _ = RefreshItemsAsync();
    }

    public void Move(int index, int direction)
    {
        var target = index + direction;
        var entries = Data.ConfigEntry.HomePageItems;
        if (index < 0 || target < 0 || index >= entries.Count || target >= entries.Count) return;
        var entry = entries[index];
        entries.RemoveAt(index);
        entries.Insert(target, entry);
        ConfigSaver.SaveConfig();
        _ = RefreshItemsAsync();
    }

    private void ConfigEntry_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(Data.ConfigEntry.UsingMinecraftMinecraftAccount) or
            nameof(Data.ConfigEntry.UsingBedrockAccount)))
            return;

        RefreshAccountSubscriptions();
        RefreshGreeting();
    }

    private void Account_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MinecraftAccount.Name) or nameof(BedrockAccount.Gamertag))
            RefreshGreeting();
    }

    private void GreetingTimer_OnTick(object? sender, EventArgs e)
    {
        RefreshGreeting();
    }

    private void Source_OnChanged(object? sender, EventArgs e) => _ = RefreshItemsAsync();

    private void InstanceManager_OnInstanceIconChanged(object? sender, MinecraftInstance instance) =>
        _ = RefreshItemsAsync();

    private async Task RefreshItemsAsync()
    {
        var version = ++_refreshVersion;
        var items = new List<HomePageGameItem>();
        foreach (var (entry, index) in Data.ConfigEntry.HomePageItems.Select((entry, index) => (entry, index)))
        {
            var instance = ResolveInstance(entry.InstanceFolderPath);
            if (instance == null) continue;

            switch (entry.ItemKind ?? InferKind(entry))
            {
                case HomePageItemKind.Instance:
                    items.Add(HomePageGameItem.ForInstance(instance, index));
                    break;
                case HomePageItemKind.Server when !string.IsNullOrWhiteSpace(entry.ServerAddress):
                    var port = entry.ServerPort ?? 25565;
                    var serverName = entry.DisplayName ?? ServerPing.BuildDisplayAddress(entry.ServerAddress, port);
                    items.Add(HomePageGameItem.ForTarget(instance, index, RecentPlayTargetType.Server,
                        entry.TargetId ?? $"server:{entry.ServerAddress}:{port}", serverName,
                        $"{ServerPing.BuildDisplayAddress(entry.ServerAddress, port)} · {instance.ShortDisplay}", true,
                        serverAddress: entry.ServerAddress, serverPort: port));
                    break;
                case HomePageItemKind.World when !string.IsNullOrWhiteSpace(entry.TargetId):
                    var world = (await new WorldSaveService().ScanAsync(instance))
                        .FirstOrDefault(item => item.FolderName == entry.TargetId);
                    if (world != null)
                        items.Add(HomePageGameItem.ForTarget(instance, index, RecentPlayTargetType.World,
                            world.FolderName,
                            string.IsNullOrWhiteSpace(world.LevelName) ? world.FolderName : world.LevelName,
                            $"{world.Version ?? "?"} · {instance.ShortDisplay}", CanQuickEnterWorld(instance), world));
                    break;
            }
        }

        if (version != _refreshVersion) return;
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        IsEmpty = Items.Count == 0;
    }

    private async Task<MinecraftInstance?> PickInstanceAsync()
    {
        var result = await OverlayDialog.ShowCustomAsync<InstancePickerDialog, InstancePickerDialogViewModel, object?>(
            new InstancePickerDialogViewModel(), _view.TryGetHostId(), CreatePickerOptions());
        return result as MinecraftInstance;
    }

    private async Task<WorldPickItem?> PickWorldAsync(MinecraftInstance instance)
    {
        var result = await OverlayDialog.ShowCustomAsync<WorldPickerDialog, WorldPickerDialogViewModel, object?>(
            new WorldPickerDialogViewModel(instance), _view.TryGetHostId(), CreatePickerOptions());
        return result as WorldPickItem;
    }

    private async Task<ServerConnectResult?> PickServerAsync(MinecraftInstance instance)
    {
        var result = await OverlayDialog.ShowCustomAsync<ServerConnectDialog, ServerConnectDialogViewModel, object?>(
            new ServerConnectDialogViewModel(instance), _view.TryGetHostId(), new OverlayDialogOptions
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

    private static HomePageItemKind InferKind(GameListWidgetEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.ServerAddress)
            ? HomePageItemKind.Server
            : !string.IsNullOrWhiteSpace(entry.TargetId)
                ? HomePageItemKind.World
                : HomePageItemKind.Instance;

    private static MinecraftInstance? ResolveInstance(string path) =>
        InstanceManager.Instance.Instances.FirstOrDefault(instance =>
            string.Equals(instance.InstanceFolderPath, path, StringComparison.OrdinalIgnoreCase));

    private static bool CanQuickEnterWorld(MinecraftInstance instance) =>
        instance.MinecraftEntry is { } entry && entry.ReleaseTime > new DateTime(2023, 4, 4);

    private void RefreshAccountSubscriptions()
    {
        SetAccountSubscriptions(Data.ConfigEntry.UsingMinecraftMinecraftAccount,
            Data.ConfigEntry.UsingBedrockAccount);
    }

    private void SetAccountSubscriptions(MinecraftAccount? javaAccount, BedrockAccount? bedrockAccount)
    {
        if (_javaAccount != null)
            _javaAccount.PropertyChanged -= Account_OnPropertyChanged;
        if (_bedrockAccount != null)
            _bedrockAccount.PropertyChanged -= Account_OnPropertyChanged;

        _javaAccount = javaAccount;
        _bedrockAccount = bedrockAccount;

        if (_javaAccount != null)
            _javaAccount.PropertyChanged += Account_OnPropertyChanged;
        if (_bedrockAccount != null)
            _bedrockAccount.PropertyChanged += Account_OnPropertyChanged;
    }

    private void RefreshGreeting()
    {
        var accountName = !string.IsNullOrWhiteSpace(_javaAccount?.Name)
            ? _javaAccount.Name
            : _bedrockAccount?.Gamertag;
        if (string.IsNullOrWhiteSpace(accountName))
        {
            Greeting = null;
            return;
        }

        var greeting = DateTime.Now.Hour switch
        {
            < 3 => CommonLanguageManager.Instance.homePage_greetingAfterMidnight.CurrentValue(),
            < 6 => CommonLanguageManager.Instance.homePage_greetingBeforeDawn.CurrentValue(),
            < 9 => CommonLanguageManager.Instance.homePage_greetingEarlyMorning.CurrentValue(),
            < 12 => CommonLanguageManager.Instance.homePage_greetingMorning.CurrentValue(),
            < 14 => CommonLanguageManager.Instance.homePage_greetingNoon.CurrentValue(),
            < 17 => CommonLanguageManager.Instance.homePage_greetingAfternoon.CurrentValue(),
            < 20 => CommonLanguageManager.Instance.homePage_greetingEvening.CurrentValue(),
            < 23 => CommonLanguageManager.Instance.homePage_greetingNight.CurrentValue(),
            _ => CommonLanguageManager.Instance.homePage_greetingLateNight.CurrentValue()
        };
        Greeting = string.Format(greeting, accountName);
    }
}

public sealed class HomePageGameItem
{
    public required MinecraftInstance Instance { get; init; }
    public required string Name { get; init; }
    public required string Details { get; init; }
    public required int Index { get; init; }
    public required bool CanPlay { get; init; }
    public RecentPlayTargetType? TargetType { get; init; }
    public string? TargetId { get; init; }
    public string? ServerAddress { get; init; }
    public int? ServerPort { get; init; }
    public WorldSaveInfo? WorldInfo { get; init; }
    public DateTime LastPlayedTime { get; init; }

    public IImage? Icon => TargetType == RecentPlayTargetType.World &&
                           WorldInfo?.IconPath is { } path && File.Exists(path)
        ? new Bitmap(path)
        : Instance[58];

    public string PlayText => TargetType switch
    {
        RecentPlayTargetType.Server => WidgetsLanguageManager.Instance.contextmenu_enterServer.CurrentValue(),
        RecentPlayTargetType.World => WidgetsLanguageManager.Instance.contextmenu_enterWorld.CurrentValue(),
        _ => WidgetsLanguageManager.Instance.contextmenu_startGame.CurrentValue()
    };

    public static HomePageGameItem ForInstance(MinecraftInstance instance, int index) => new()
    {
        Instance = instance, Index = index, Name = instance.InstanceName,
        Details = $"{instance.ShortDisplay} · {instance.DisplayLastPlayTime}", CanPlay = true,
        LastPlayedTime = instance.LastPlayTime
    };

    public static HomePageGameItem ForTarget(MinecraftInstance instance, int index, RecentPlayTargetType type,
        string id, string name, string details, bool canPlay, WorldSaveInfo? worldInfo = null,
        string? serverAddress = null, int? serverPort = null) => new()
    {
        Instance = instance, Index = index, TargetType = type, TargetId = id, Name = name,
        Details = details, CanPlay = canPlay, WorldInfo = worldInfo, ServerAddress = serverAddress,
        ServerPort = serverPort, LastPlayedTime = worldInfo?.LastPlayedTime ?? DateTime.MinValue
    };
}

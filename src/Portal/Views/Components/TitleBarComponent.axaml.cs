using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Module.Multiplayer;
using Portal.Core.Module.Update;
using Portal.Localization;
using Portal.Views.Components.Operations.Account;
using Portal.Views.Pages;
using Portal.Views.Pages.DownloadPages;
using Portal.Views.Pages.SettingPages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Gateway;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Common.Classes;
using TioUi.Common.Extensions;
using TioUi.Controls;
using TioUi.Controls.Options;

namespace Portal.Views.Components;

public partial class TitleBarComponent : Grid
{
    private const double TaskTitleScrollStep = 0.7;
    private const double TaskTitleGap = 24;

    public static readonly StyledProperty<string?> DropMsgProperty =
        AvaloniaProperty.Register<TitleBarComponent, string?>(nameof(DropMsg));

    private readonly DispatcherTimer _taskTitleScrollTimer;
    private double _taskTitleCycle;

    public TitleBarComponent()
    {
        InitializeComponent();
        DataContext = this;
        _taskTitleScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        _taskTitleScrollTimer.Tick += TaskTitleScrollTimer_OnTick;
        CurrentTaskTitleText.PropertyChanged += CurrentTaskTitleText_OnPropertyChanged;
        CurrentTaskTitleText.SizeChanged += (_, _) => UpdateTaskTitleAnimation();
        Loaded += (_, _) => Dispatcher.UIThread.Post(UpdateTaskTitleAnimation, DispatcherPriority.Render);
        DetachedFromVisualTree += (_, _) => _taskTitleScrollTimer.Stop();
    }

    public string? DropMsg
    {
        get => GetValue(DropMsgProperty);
        set => SetValue(DropMsgProperty, value);
    }

    public Data Data { get; set; } = Data.Instance;

    public bool IsWindows => OperatingSystem.IsWindows();

    public TaskManager Tasks => TaskManager.Instance;

    private void CurrentTaskTitleText_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBlock.TextProperty)
            Dispatcher.UIThread.Post(UpdateTaskTitleAnimation, DispatcherPriority.Render);
    }

    private void UpdateTaskTitleAnimation()
    {
        TaskTitleScrollViewer.Offset = default;
        TaskTitleLoopText.IsVisible = false;
        Dispatcher.UIThread.Post(UpdateTaskTitleOverflow, DispatcherPriority.Render);
    }

    private void UpdateTaskTitleOverflow()
    {
        var titleWidth = CurrentTaskTitleText.Bounds.Width;
        if (titleWidth <= TaskTitleScrollViewer.Viewport.Width)
        {
            _taskTitleCycle = 0;
            _taskTitleScrollTimer.Stop();
            return;
        }

        TaskTitleLoopText.IsVisible = true;
        _taskTitleCycle = titleWidth + TaskTitleGap;
        _taskTitleScrollTimer.Start();
    }

    private void TaskTitleScrollTimer_OnTick(object? sender, EventArgs e)
    {
        if (_taskTitleCycle <= 0) return;

        var nextOffset = TaskTitleScrollViewer.Offset.X + TaskTitleScrollStep;
        if (nextOffset >= _taskTitleCycle)
            nextOffset -= _taskTitleCycle;

        TaskTitleScrollViewer.Offset = new Vector(nextOffset, 0);
    }

    private void OpenTasks(object? sender, RoutedEventArgs e)
    {
        var hostId = Root.TryGetHostId();
        _ = OverlayDrawer.ShowStandardAsync(new TaskDrawerView(), null, hostId, new DrawerOptions
        {
            Title = CommonLanguageManager.Instance.titleBar_tasks.CurrentValue(),
            TitleCommand = new RelayCommand(OpenTaskTab),
            Buttons = DialogButton.None,
            MinWidth = 500,
            CanResize = false
        });
    }

    private void OpenTaskTab()
    {
        if (Root.GetTopLevel() is not TioTabWindowBase window) return;

        var tab = new TabEntry(window, new TaskPage());
        window.CreateTab(tab);
        window.SelectTab(tab);
    }

    private void ThemeMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not string themeName) return;

        Data.ConfigEntry.Theme = themeName switch
        {
            "System" => TioUi.Shared.Theme.System,
            "Light" => TioUi.Shared.Theme.Light,
            "Dark" => TioUi.Shared.Theme.Dark,
            "Mirage" => TioUi.Shared.Theme.Mirage,
            _ => Data.ConfigEntry.Theme
        };
    }

    private async void AccountButton_Click(object? sender, RoutedEventArgs e)
    {
        AccountFlyout.Flyout.ShowAt(AccountFlyoutPoint);
    }

    private async void AddAcountButton_OnClick(object? sender, RoutedEventArgs e)
    {
        AccountFlyout.Flyout.Hide();
        var tryGetHostId = Root!.TryGetHostId()!;
        var result = await AddAccount.Main(tryGetHostId, Data.ConfigEntry.AuthServers);
        if (result == null) return;
        foreach (var minecraftAccount in result.JavaAccounts) Data.ConfigEntry.MinecraftAccounts.Add(minecraftAccount);
        if (result.JavaAccounts.Count > 0)
            Data.ConfigEntry.UsingMinecraftMinecraftAccount = result.JavaAccounts[^1];
        if (result.BedrockAccount is { } bedrockAccount)
        {
            var existing = Data.ConfigEntry.BedrockAccounts.FirstOrDefault(item => item.Xuid == bedrockAccount.Xuid);
            if (existing != null) Data.ConfigEntry.BedrockAccounts.Remove(existing);
            Data.ConfigEntry.BedrockAccounts.Add(bedrockAccount);
            Data.ConfigEntry.UsingBedrockAccount = bedrockAccount;
        }
    }

    public void DeleteAccount(object parameter)
    {
        if (parameter is not MinecraftAccount account) return;
        if (Data.ConfigEntry.UsingMinecraftMinecraftAccount == account)
        {
            Data.ConfigEntry.MinecraftAccounts.Remove(account);
            Data.ConfigEntry.UsingMinecraftMinecraftAccount = Data.ConfigEntry.MinecraftAccounts.FirstOrDefault();
        }
        else
        {
            Data.ConfigEntry.MinecraftAccounts.Remove(account);
        }

        Root.GetTopLevel().Notice(new NotificationOptions
        {
            Content = string.Format(
                CommonLanguageManager.Instance.titleBar_removedAccount.CurrentValue(), account.Name,
                account.DisplayAccountNote),
            Type = NotificationType.Success,
            Expiration = TimeSpan.FromSeconds(3),
            OperateButtons =
            [
                new OperateButtonEntry(CommonLanguageManager.Instance.titleBar_undo.CurrentValue(), _ =>
                {
                    Data.ConfigEntry.MinecraftAccounts.Add(account);
                    Data.ConfigEntry.UsingMinecraftMinecraftAccount = account;
                }, true)
            ]
        });

        if (Data.ConfigEntry.MinecraftAccounts.Count == 0)
            AccountFlyout.Flyout.Hide();
    }

    private void DeleteBedrockAccount_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: BedrockAccount account }) return;
        Data.ConfigEntry.BedrockAccounts.Remove(account);
        if (Data.ConfigEntry.UsingBedrockAccount == account)
            Data.ConfigEntry.UsingBedrockAccount = Data.ConfigEntry.BedrockAccounts.FirstOrDefault();
        if (!Data.ConfigEntry.HasAnyAccounts) AccountFlyout.Flyout.Hide();
    }

    private void OpenSearch(object? sender, RoutedEventArgs e)
    {
        var options = new DialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            StyleClass = "undrag",
            CanResize = true,
            StartupLocation = WindowStartupLocation.CenterOwner,
            DialogWindowMinWidth = 770,
            DialogWindowMinHeight = 471,
            DialogWindowWidth = 770,
            DialogWindowHeight = 471,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        _ = Dialog.ShowCustomAsync<AggregatedSearchDialog, AggregatedSearchDialogViewModel, object>(
            new AggregatedSearchDialogViewModel((Root.GetTopLevel() as TioTabWindowBase)!), options: options,
            owner: (Root.GetTopLevel() as TioTabWindowBase)!);
    }

    private void SettingMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        var tioTabWindowBase = Root.GetTopLevel() as TioTabWindowBase;
        var tabEntry = new TabEntry(tioTabWindowBase!, new SettingPage());
        tioTabWindowBase.CreateTab(tabEntry);
        tioTabWindowBase.SelectTab(tabEntry);
    }

    private void DownloadButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var tioTabWindowBase = Root.GetTopLevel() as TioTabWindowBase;
        var tabEntry = new TabEntry(tioTabWindowBase!, new DownloadPage());
        tioTabWindowBase.CreateTab(tabEntry);
        tioTabWindowBase.SelectTab(tabEntry);
    }

    private void ToolsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var tioTabWindowBase = Root.GetTopLevel() as TioTabWindowBase;
        var tabEntry = new TabEntry(tioTabWindowBase!, new ToolsPage());
        tioTabWindowBase.CreateTab(tabEntry);
        tioTabWindowBase.SelectTab(tabEntry);
    }

    private async void OpenCreateInstance(object? sender, RoutedEventArgs e)
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
            new CreateInstanceDialogViewModel(), Root.TryGetHostId(), options);
    }

    private void OpenMultiplayer(object? sender, RoutedEventArgs e)
    {
        if (Root.GetTopLevel() is not TioTabWindowBase window) return;
        var tabEntry = new TabEntry(window, new MultiplayerPage(MinecraftEdition.Java));
        window.CreateTab(tabEntry);
        window.SelectTab(tabEntry);
    }

    private void OpenMultiplayerEdition(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string editionName } ||
            Root.GetTopLevel() is not TioTabWindowBase window) return;

        var edition = editionName == "Bedrock" ? MinecraftEdition.Bedrock : MinecraftEdition.Java;
        var tabEntry = new TabEntry(window, new MultiplayerPage(edition));
        window.CreateTab(tabEntry);
        window.SelectTab(tabEntry);
    }

    private void GoToAbout(object? sender, RoutedEventArgs e)
    {
        var tioTabWindowBase = Root.GetTopLevel() as TioTabWindowBase;
        var tioTabPage = new SettingPage();
        tioTabPage.SettingPageViewModel.NavigateType(typeof(About));
        tioTabPage.NavMenu.SelectedItem = tioTabPage.AboutItem;
        var tabEntry = new TabEntry(tioTabWindowBase!, tioTabPage);
        tioTabWindowBase.CreateTab(tabEntry);
        tioTabWindowBase.SelectTab(tabEntry);
    }

    private void ShowUpdateDownloadingNotice(object? sender, RoutedEventArgs e)
    {
        Root.GetTopLevel().Notice(CommonLanguageManager.Instance.update_packageDownloading.CurrentValue());
    }

    private async void RestartUpdate(object? sender, RoutedEventArgs e)
    {
        if (UpdateApp.ReadyUpdate is not { } update) return;
        try
        {
            await UpdateApp.Apply(update);
        }
        catch (Exception exception)
        {
            Logger.Error(LogLanguageManager.Instance.about_updateStartFailed.CurrentValue(), exception);
            Root.GetTopLevel().Notice(string.Format(
                CommonLanguageManager.Instance.about_cannotStartUpdate.CurrentValue(), exception.Message),
                NotificationType.Error);
        }
    }

    private async void ChangeSkin_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not MinecraftAccount account) return;

        AccountFlyout.Flyout.Hide();
        var hostId = Root!.TryGetHostId();
        var result = await ChangeSkinDialog.Show(hostId, null);
        if (!string.IsNullOrEmpty(result) && File.Exists(result))
            account.Skin = Convert.ToBase64String(await File.ReadAllBytesAsync(result));
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        AccountFlyout.Flyout.Hide();
        var tioTabWindowBase = Root.GetTopLevel() as TioTabWindowBase;
        var tioTabPage = new SettingPage();
        tioTabPage.NavigateTo(typeof(Account));
        var tabEntry = new TabEntry(tioTabWindowBase!, tioTabPage);
        tioTabWindowBase.CreateTab(tabEntry);
        tioTabWindowBase.SelectTab(tabEntry);
    }
}

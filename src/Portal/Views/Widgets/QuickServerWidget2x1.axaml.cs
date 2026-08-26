using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Portal.Core.Classes.Entries;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Module.Widgets;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Portal.Views.Pages;

namespace Portal.Views.Widgets;

public partial class QuickServerWidget2x1 : InstanceBoundWidgetBase
{
    private readonly ServerPing _ping = new();
    private string? _pingAddress;

    protected override IReadOnlyList<(WidgetClickAction Action, string Header)> ClickActionOptions =>
    [
        (WidgetClickAction.None, WidgetsLanguageManager.Instance.contextmenu_noAction.CurrentValue()),
        (WidgetClickAction.QuickEnterServer, WidgetsLanguageManager.Instance.contextmenu_quickEnterServer.CurrentValue())
    ];

    protected override void PlayFromContextMenu()
    {
        QuickEnterServer();
    }

    public QuickServerWidget2x1()
    {
        Size = new WidgetCellSize(2, 1);
        InitializeComponent();
        _ping.Changed += OnPingChanged;
    }

    protected override void OnInstanceResolved()
    {
        RefreshDisplay();
    }

    protected override void OnInstanceIconRefreshed()
    {
        RefreshDisplay();
    }

    private void OnPingChanged()
    {
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        var iconImage = this.FindControl<Image>("IconImage");
        var statusText = this.FindControl<TextBlock>("StatusText");
        var statusDot = this.FindControl<Ellipse>("StatusDot");
        var pingText = this.FindControl<TextBlock>("PingText");
        var playersText = this.FindControl<TextBlock>("PlayersText");
        var addressText = this.FindControl<TextBlock>("AddressText");
        var instanceText = this.FindControl<TextBlock>("InstanceText");
        var hintText = this.FindControl<TextBlock>("HintText");

        var instance = Instance;
        var data = GetData<QuickServerWidgetData>();
        var address = data?.ServerAddress;
        var port = data?.ServerPort ?? 25565;

        if (iconImage != null) iconImage.Source = instance?[40];
        if (instanceText != null) instanceText.Text = instance?.ShortDisplay ?? string.Empty;

        if (statusText != null)
        {
            statusText.Text = _ping.StatusText;
            statusText.Foreground = _ping.StatusBrush;
        }

        if (statusDot != null) statusDot.Fill = _ping.StatusBrush;
        if (pingText != null)
        {
            pingText.Text = _ping.PingText;
            pingText.Foreground = _ping.PingBrush;
            pingText.IsVisible = _ping.HasPing;
        }

        if (playersText != null)
        {
            playersText.Text = _ping.PlayersText;
            playersText.IsVisible = _ping.HasPlayers;
        }

        if (string.IsNullOrEmpty(address))
        {
            if (addressText != null)
                addressText.Text = string.IsNullOrEmpty(instance?.InstanceName)
                    ? CommonLanguageManager.Instance.widgets_serverNotConfigured.CurrentValue()
                    : instance.InstanceName!;
            if (hintText != null) hintText.Text = string.Empty;
            return;
        }

        var fullAddress = ServerPing.BuildAddress(address, port);
        if (!string.Equals(_pingAddress, fullAddress, StringComparison.Ordinal))
        {
            _pingAddress = fullAddress;
            _ping.Start(fullAddress);
        }

        if (addressText != null) addressText.Text = ServerPing.BuildDisplayAddress(address, port);
        if (hintText != null) hintText.Text = CommonLanguageManager.Instance.widgets_quickEnterHint.CurrentValue();
    }

    private void LaunchButton_OnClick(object? sender, RoutedEventArgs e)
    {
        QuickEnterServer();
    }

    public override void PerformClick()
    {
        if (ClickAction == WidgetClickAction.QuickEnterServer)
            QuickEnterServer();
    }

    private void QuickEnterServer()
    {
        var data = GetData<QuickServerWidgetData>();
        if (Instance == null || string.IsNullOrEmpty(data?.ServerAddress))
            return;
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var address = data!.ServerAddress!;
        var port = data!.ServerPort ?? 25565;

        var target = new RecentPlayTarget(
            Instance,
            RecentPlayTargetType.Server,
            $"{address}:{port}",
            address,
            string.Format(CommonLanguageManager.Instance.recentPlay_serverDescription.CurrentValue(),
                Instance.InstanceName),
            DateTime.MinValue,
            ServerAddress: address,
            ServerPort: port);

        _ = MinecraftLaunchService.LaunchAsync(Instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(Instance, logSession => MinecraftLogPage.Open(logSession, topLevel)),
            target);
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Module.Widgets;
using Portal.Services;
using Portal.Views.Pages;

namespace Portal.Views.Widgets;

public partial class QuickServerWidget2x1 : InstanceBoundWidgetBase
{
    public QuickServerWidget2x1()
    {
        Size = new WidgetCellSize(2, 1);
        InitializeComponent();
    }

    protected override void OnInstanceResolved() => RefreshDisplay();

    protected override void OnInstanceIconRefreshed() => RefreshDisplay();

    private void RefreshDisplay()
    {
        var iconImage = this.FindControl<Image>("IconImage");
        var titleText = this.FindControl<TextBlock>("TitleText");
        var addressText = this.FindControl<TextBlock>("AddressText");
        var instanceText = this.FindControl<TextBlock>("InstanceText");
        var hintText = this.FindControl<TextBlock>("HintText");

        var instance = Instance;
        var address = LayoutData?.ServerAddress;
        var port = LayoutData?.ServerPort;

        if (instance == null)
        {
            if (iconImage != null) iconImage.Source = null;
            if (instanceText != null) instanceText.Text = string.Empty;
        }
        else
        {
            if (iconImage != null) iconImage.Source = instance[40];
            if (instanceText != null) instanceText.Text = instance.ShortDisplay;
        }

        if (string.IsNullOrEmpty(address))
        {
            if (titleText != null) titleText.Text = "未配置服务器";
            if (addressText != null) addressText.Text = string.Empty;
            if (hintText != null) hintText.Text = string.Empty;
            return;
        }

        if (titleText != null) titleText.Text = address;
        if (addressText != null)
            addressText.Text = port is { } p ? $"{address}:{p}" : address;
        if (hintText != null) hintText.Text = "点击右侧按钮快速进入";
    }

    private void LaunchButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Instance == null || string.IsNullOrEmpty(LayoutData?.ServerAddress))
            return;
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var address = LayoutData!.ServerAddress!;
        var port = LayoutData!.ServerPort ?? 25565;

        var target = new RecentPlayTarget(
            Instance,
            RecentPlayTargetType.Server,
            $"{address}:{port}",
            address,
            $"服务器·{Instance.InstanceName}",
            DateTime.MinValue,
            ServerAddress: address,
            ServerPort: port);

        _ = MinecraftLaunchService.LaunchAsync(Instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(logSession => MinecraftLogPage.Open(logSession, topLevel)), target);
    }
}

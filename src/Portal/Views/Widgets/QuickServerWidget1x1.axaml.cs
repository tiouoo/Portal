using Avalonia.Controls;
using Avalonia.Interactivity;
using Portal.Classes.Entries;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Module.Widgets;
using Portal.Services;
using Portal.Views.Pages;

namespace Portal.Views.Widgets;

public partial class QuickServerWidget1x1 : InstanceBoundWidgetBase
{
    public QuickServerWidget1x1()
    {
        Size = new WidgetCellSize(1, 1);
        InitializeComponent();
    }

    protected override void OnInstanceResolved() => RefreshDisplay();

    protected override void OnInstanceIconRefreshed() => RefreshDisplay();

    private void RefreshDisplay()
    {
        var iconImage = this.FindControl<Image>("IconImage");
        var titleText = this.FindControl<TextBlock>("TitleText");
        var sourceText = this.FindControl<TextBlock>("SourceText");

        var instance = Instance;
        var address = GetData<QuickServerWidgetData>()?.ServerAddress;

        if (instance == null)
        {
            if (iconImage != null) iconImage.Source = null;
            if (sourceText != null) sourceText.Text = string.Empty;
        }
        else
        {
            if (iconImage != null) iconImage.Source = instance[56];
            if (sourceText != null) sourceText.Text = instance.InstanceName;
        }

        if (string.IsNullOrEmpty(address))
        {
            if (titleText != null) titleText.Text = "未配置服务器";
            return;
        }

        if (titleText != null) titleText.Text = address;
    }

    private void LaunchButton_OnClick(object? sender, RoutedEventArgs e)
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
            $"服务器·{Instance.InstanceName}",
            DateTime.MinValue,
            ServerAddress: address,
            ServerPort: port);

        _ = MinecraftLaunchService.LaunchAsync(Instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(logSession => MinecraftLogPage.Open(logSession, topLevel)), target);
    }
}

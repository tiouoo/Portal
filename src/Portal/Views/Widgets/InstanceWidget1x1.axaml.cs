using Avalonia.Controls;
using Avalonia.Interactivity;
using Portal.Core.Minecraft;
using Portal.Core.Module.Widgets;
using Portal.Core.Services;
using Portal.Views.Pages;

namespace Portal.Views.Widgets;

public partial class InstanceWidget1x1 : InstanceBoundWidgetBase
{
    public InstanceWidget1x1()
    {
        Size = new WidgetCellSize(1, 1);
        InitializeComponent();
    }

    protected override void OnInstanceResolved()
    {
        RefreshDisplay();
    }

    protected override void OnInstanceIconRefreshed()
    {
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        var iconImage = this.FindControl<Image>("IconImage");
        var titleText = this.FindControl<TextBlock>("TitleText");
        var sourceText = this.FindControl<TextBlock>("SourceText");

        var instance = Instance;
        if (instance == null)
        {
            if (iconImage != null) iconImage.Source = null;
            if (titleText != null) titleText.Text = "未选择实例";
            if (sourceText != null) sourceText.Text = string.Empty;
            return;
        }

        if (iconImage != null) iconImage.Source = instance[56];
        if (titleText != null) titleText.Text = instance.InstanceName;
        if (sourceText != null) sourceText.Text = instance.ShortDisplay;
    }

    public override void PerformClick()
    {
        if (Instance == null)
            return;
        if (TopLevel.GetTopLevel(this) is { } topLevel)
            InstanceDetailPage.Open(Instance, topLevel);
    }

    private void LaunchButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Instance == null)
            return;
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        _ = MinecraftLaunchService.LaunchAsync(Instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(Instance, logSession => MinecraftLogPage.Open(logSession, topLevel)));
    }
}
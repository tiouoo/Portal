using Avalonia.Controls;
using Avalonia.Interactivity;
using Portal.Core.Minecraft;
using Portal.Core.Module.Widgets;
using Portal.Core.Minecraft.Services;
using Portal.Views.Pages;

namespace Portal.Views.Widgets;

public partial class InstanceWidget2x1 : InstanceBoundWidgetBase
{
    public InstanceWidget2x1()
    {
        Size = new WidgetCellSize(2, 1);
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
        var folderText = this.FindControl<TextBlock>("FolderText");
        var versionText = this.FindControl<TextBlock>("VersionText");
        var lastPlayText = this.FindControl<TextBlock>("LastPlayText");

        var instance = Instance;
        if (instance == null)
        {
            if (iconImage != null) iconImage.Source = null;
            if (titleText != null) titleText.Text = "未选择实例";
            if (folderText != null) folderText.Text = string.Empty;
            if (versionText != null) versionText.Text = string.Empty;
            if (lastPlayText != null) lastPlayText.Text = string.Empty;
            return;
        }

        if (iconImage != null) iconImage.Source = instance[40];
        if (titleText != null) titleText.Text = instance.InstanceName;
        if (folderText != null) folderText.Text = instance.FolderName;
        if (versionText != null) versionText.Text = instance.ShortDisplay;
        if (lastPlayText != null) lastPlayText.Text = instance.DisplayLastPlayTime;
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
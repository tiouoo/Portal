using Avalonia.Controls;
using Avalonia.Interactivity;
using Portal.Core.Classes.Entries;
using Portal.Core.Minecraft;
using Portal.Core.Module.Widgets;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Portal.Views.Pages;

namespace Portal.Views.Widgets;

public partial class InstanceWidget2x1 : InstanceBoundWidgetBase
{
    protected override WidgetClickAction DefaultClickAction => WidgetClickAction.ShowDetails;
    protected override IReadOnlyList<(WidgetClickAction Action, string Header)> ClickActionOptions =>
        CanQuickEnterWorld
            ?
            [
                (WidgetClickAction.ShowDetails, WidgetsLanguageManager.Instance.contextmenu_showInstanceDetails.CurrentValue()),
                (WidgetClickAction.LaunchInstance, WidgetsLanguageManager.Instance.contextmenu_launchInstance.CurrentValue()),
                (WidgetClickAction.QuickEnterWorld, WidgetsLanguageManager.Instance.contextmenu_quickEnterWorld.CurrentValue())
            ]
            :
            [
                (WidgetClickAction.ShowDetails, WidgetsLanguageManager.Instance.contextmenu_showInstanceDetails.CurrentValue()),
                (WidgetClickAction.LaunchInstance, WidgetsLanguageManager.Instance.contextmenu_launchInstance.CurrentValue())
            ];

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
            if (titleText != null) titleText.Text = CommonLanguageManager.Instance.widgets_noInstance.CurrentValue();
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
        switch (ClickAction)
        {
            case WidgetClickAction.LaunchInstance:
                LaunchInstance();
                break;
            case WidgetClickAction.QuickEnterWorld when CanQuickEnterWorld:
                _ = PickAndQuickEnterWorldAsync();
                break;
            default:
                OpenInstanceDetails();
                break;
        }
    }

    private void LaunchButton_OnClick(object? sender, RoutedEventArgs e)
    {
        LaunchInstance();
    }
}

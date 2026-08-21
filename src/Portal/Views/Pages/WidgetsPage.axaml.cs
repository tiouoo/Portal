using Avalonia.Controls;
using Avalonia.Media;
using Portal.Core.Module.AggregatedSearch;
using Portal.Module.DefaultPage;
using Portal.Localization;
using Portal.Views.Widgets;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

using Portal.Module;
namespace Portal.Views.Pages;

[DefaultPage("pages_widgets")]
[AggregatedSearchPage("pages_widgets", "pages_widgetsPath", "Widgets")]
public partial class WidgetsPage : UserControl, ITioTabPage
{
    private readonly WidgetWorkspace? _workspace;

    public WidgetsPage()
    {
        InitializeComponent();
        _workspace = new WidgetWorkspace();
        _workspace.AddWidgetCallOn += OnAddWidgetCallOn;
        if (this.FindControl<ContentControl>("WorkspaceHost") is { } host)
            host.Content = _workspace;
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = CommonLanguageManager.Instance.widgetsPage_pageTitle.CurrentValue(),
        Icon = GeometryResources.Get("GridGeometry")
    };

    public TabEntry HostTab { get; set; }

    public void OnClose()
    {
        DataContext = null;
    }

    private void OnAddWidgetCallOn(object? sender, EventArgs e)
    {
        OpenAddWidgetDialog();
    }

    private void OpenAddWidgetDialog()
    {
        if (_workspace == null)
            return;

        var options = new OverlayDialogOptions
        {
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = false,
            CanResize = false,
            IsCloseButtonVisible = false,
            StyleClass = "undrag"
        };
        _ = OverlayDialog.ShowCustomAsync<AddWidgetDialog, AddWidgetDialogViewModel, bool>(
            new AddWidgetDialogViewModel(_workspace, this.TryGetHostId()), this.TryGetHostId(), options);
    }
}
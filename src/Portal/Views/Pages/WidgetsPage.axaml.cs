using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using Portal.Core.Module.AggregatedSearch;
using Portal.Module.AggregatedSearch;
using Portal.Module.DefaultPage;
using Portal.Views.Widgets;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages;

[DefaultPage("小组件")]
[AggregatedSearchPage("小组件", "小组件", "Widgets")]
public partial class WidgetsPage : UserControl, ITioTabPage
{
    private WidgetWorkspace? _workspace;

    public WidgetsPage()
    {
        InitializeComponent();
        _workspace = new WidgetWorkspace();
        _workspace.AddWidgetCallOn += OnAddWidgetCallOn;
        if (this.FindControl<ContentControl>("WorkspaceHost") is { } host)
            host.Content = _workspace;
    }

    private void OnAddWidgetCallOn(object? sender, System.EventArgs e) => OpenAddWidgetDialog();

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
            new AddWidgetDialogViewModel(_workspace, this.TryGetHostId()), hostId: this.TryGetHostId(), options: options);
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = "小组件",
        Icon = StreamGeometry.Parse(
            "F1 M640,640z M0,0z M96,128C96,110 110,96 128,96L256,96C274,96 288,110 288,128L288,256C288,274 274,288 256,288L128,288C110,288 96,274 96,256L96,128z M96,384C96,366 110,352 128,352L256,352C274,352 288,366 288,384L288,512C288,530 274,544 256,544L128,544C110,544 96,530 96,512L96,384z M352,128C352,110 366,96 384,96L512,96C530,96 544,110 544,128L544,256C544,274 530,288 512,288L384,288C366,288 352,274 352,256L352,128z M352,384C352,366 366,352 384,352L512,352C530,352 544,366 544,384L544,512C544,530 530,544 512,544L384,544C366,544 352,530 352,512L352,384z")
    };

    public TabEntry HostTab { get; set; }

    public void OnClose()
    {
        DataContext = null;
    }
}

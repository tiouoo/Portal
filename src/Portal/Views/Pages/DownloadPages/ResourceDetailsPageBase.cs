using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Controls;

namespace Portal.Views.Pages.DownloadPages;

public abstract class ResourceDetailsPageBase : UserControl, ITioTabPage
{
    private bool _isWaitingForTargetVersionGroup;
    private object? _targetVersionGroup;

    public PageInfo PageInfo { get; init; } = null!;
    public TabEntry HostTab { get; set; } = null!;

    protected void QueueScrollTo(object? group)
    {
        _targetVersionGroup = group;
        if (_isWaitingForTargetVersionGroup) return;
        _isWaitingForTargetVersionGroup = true;
        LayoutUpdated += OnLayoutUpdated;
    }

    protected void CancelScroll()
    {
        LayoutUpdated -= OnLayoutUpdated;
        _targetVersionGroup = null;
        _isWaitingForTargetVersionGroup = false;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_targetVersionGroup is null) return;
        var expander = this.GetVisualDescendants().OfType<TioExpander>()
            .FirstOrDefault(control => ReferenceEquals(control.DataContext, _targetVersionGroup));
        if (expander is null) return;
        CancelScroll();
        expander.IsExpanded = true;
        Dispatcher.UIThread.Post(() => expander.BringIntoView(), DispatcherPriority.Render);
    }

    public virtual void OnClose()
    {
        CancelScroll();
        DataContext = null;
    }

    protected static void OpenTab(TopLevel sender, ResourceDetailsPageBase page, string title)
    {
        if (sender is not TioTabWindowBase window) return;
        var tab = new TabEntry(window, page, title: title);
        window.CreateTab(tab);
        window.SelectTab(tab);
    }
}

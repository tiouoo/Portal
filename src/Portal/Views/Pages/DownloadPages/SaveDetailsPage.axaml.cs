using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Controls;

namespace Portal.Views.Pages.DownloadPages;

public partial class SaveDetailsPage : UserControl, ITioTabPage
{
    private JavaResourceVersionGroup? _targetVersionGroup;
    private bool _isWaitingForTargetVersionGroup;

    public SaveDetailsPage() : this(new JavaResourceDetailsTarget(JavaResourceDefinitions.Save, ModDetailsSource.CurseForge, string.Empty)) { }
    public SaveDetailsPage(JavaResourceDetailsTarget target)
    {
        InitializeComponent(); ViewModel = new SaveDetailsPageViewModel(target); DataContext = ViewModel;
        ViewModel.TargetVersionGroupReady += ScrollToTargetVersionGroup;
        PageInfo = new PageInfo { Title = "存档详情", Icon = StreamGeometry.Parse(JavaResourceDetailsIcon.Data) };
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    public SaveDetailsPageViewModel ViewModel { get; }
    public PageInfo PageInfo { get; init; }
    public TabEntry HostTab { get; set; }
    public void OnClose() => ViewModel.Dispose();

    private void ScrollToTargetVersionGroup(JavaResourceVersionGroup group)
    {
        _targetVersionGroup = group;
        if (_isWaitingForTargetVersionGroup) return;
        _isWaitingForTargetVersionGroup = true;
        LayoutUpdated += OnLayoutUpdated;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_targetVersionGroup is null) return;
        var expander = this.GetVisualDescendants().OfType<TioExpander>().FirstOrDefault(control => ReferenceEquals(control.DataContext, _targetVersionGroup));
        if (expander is null) return;
        LayoutUpdated -= OnLayoutUpdated; _isWaitingForTargetVersionGroup = false; _targetVersionGroup = null;
        expander.IsExpanded = true;
        Dispatcher.UIThread.Post(() => expander.BringIntoView(), DispatcherPriority.Render);
    }

    private async void VersionFile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: JavaResourceFileItem file } && TopLevel.GetTopLevel(this) is { } topLevel)
            await JavaResourceDownload.ShowInstallDialogAsync(topLevel, JavaResourceDefinitions.Save, file);
    }

    public static void Open(TopLevel sender, JavaResourceDetailsTarget target, string title)
    {
        if (sender is not TioTabWindowBase window || string.IsNullOrWhiteSpace(target.ProjectId)) return;
        var tab = new TabEntry(window, new SaveDetailsPage(target), title: title); window.CreateTab(tab); window.SelectTab(tab);
    }
}

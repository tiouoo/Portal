using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Controls;

namespace Portal.Views.Pages.DownloadPages;

public partial class BedrockResourceDetailsPage : UserControl, ITioTabPage
{
    private JavaResourceVersionGroup? _targetVersionGroup;
    public BedrockResourceDetailsPage() : this(new JavaResourceDetailsTarget(BedrockResourceDefinitions.BehaviorPack, ModDetailsSource.CurseForge, string.Empty)) { }
    public BedrockResourceDetailsPage(JavaResourceDetailsTarget target)
    {
        InitializeComponent(); ViewModel = new BedrockResourceDetailsPageViewModel(target); DataContext = ViewModel;
        ViewModel.TargetVersionGroupReady += group => { _targetVersionGroup = group; LayoutUpdated += OnLayoutUpdated; };
        PageInfo = new PageInfo { Title = $"{target.Definition.DisplayName}详情", Icon = StreamGeometry.Parse(JavaResourceDetailsIcon.Data) };
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }
    public BedrockResourceDetailsPageViewModel ViewModel { get; }
    public PageInfo PageInfo { get; init; }
    public TabEntry HostTab { get; set; }
    public void OnClose() => ViewModel.Dispose();
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_targetVersionGroup is null) return;
        var expander = this.GetVisualDescendants().OfType<TioExpander>().FirstOrDefault(control => ReferenceEquals(control.DataContext, _targetVersionGroup));
        if (expander is null) return;
        LayoutUpdated -= OnLayoutUpdated; _targetVersionGroup = null; expander.IsExpanded = true;
        Dispatcher.UIThread.Post(() => expander.BringIntoView(), DispatcherPriority.Render);
    }
    private async void VersionFile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: JavaResourceFileItem file } && TopLevel.GetTopLevel(this) is { } topLevel)
            await BedrockResourceDownload.DownloadAsync(topLevel, ViewModel.Target.Definition, file);
    }
    public static void Open(TopLevel sender, JavaResourceDetailsTarget target, string title)
    {
        if (sender is not TioTabWindowBase window || string.IsNullOrWhiteSpace(target.ProjectId)) return;
        var tab = new TabEntry(window, new BedrockResourceDetailsPage(target), title: title); window.CreateTab(tab); window.SelectTab(tab);
    }
}

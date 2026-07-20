using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;

namespace Portal.Views.Pages.DownloadPages;

public partial class ResourcePackDetailsPage : UserControl, ITioTabPage
{
    public ResourcePackDetailsPage() : this(new JavaResourceDetailsTarget(JavaResourceDefinitions.ResourcePack, ModDetailsSource.Modrinth, string.Empty)) { }
    public ResourcePackDetailsPage(JavaResourceDetailsTarget target)
    {
        InitializeComponent(); ViewModel = new ResourcePackDetailsPageViewModel(target); DataContext = ViewModel;
        PageInfo = new PageInfo { Title = "材质包详情", Icon = StreamGeometry.Parse(JavaResourceDetailsIcon.Data) };
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }
    public ResourcePackDetailsPageViewModel ViewModel { get; }
    public PageInfo PageInfo { get; init; }
    public TabEntry HostTab { get; set; }
    public void OnClose() => ViewModel.Dispose();
    private async void VersionFile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: JavaResourceFileItem file } && TopLevel.GetTopLevel(this) is { } topLevel)
            await JavaResourceDownload.ShowInstallDialogAsync(topLevel, JavaResourceDefinitions.ResourcePack, file);
    }
    public static void Open(TopLevel sender, JavaResourceDetailsTarget target, string title)
    {
        if (sender is not TioTabWindowBase window || string.IsNullOrWhiteSpace(target.ProjectId)) return;
        var tab = new TabEntry(window, new ResourcePackDetailsPage(target), title: title); window.CreateTab(tab); window.SelectTab(tab);
    }
}

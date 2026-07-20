using Avalonia.Controls;
using Avalonia.Media;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;

namespace Portal.Views.Pages.DownloadPages;

public partial class ModpackDetailsPage : UserControl, ITioTabPage
{
    public ModpackDetailsPage() : this(new JavaResourceDetailsTarget(JavaResourceDefinitions.Modpack, ModDetailsSource.Modrinth, string.Empty)) { }
    public ModpackDetailsPage(JavaResourceDetailsTarget target)
    {
        InitializeComponent(); ViewModel = new ModpackDetailsPageViewModel(target); DataContext = ViewModel;
        PageInfo = new PageInfo { Title = "整合包详情", Icon = StreamGeometry.Parse(JavaResourceDetailsIcon.Data) };
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }
    public ModpackDetailsPageViewModel ViewModel { get; }
    public PageInfo PageInfo { get; init; }
    public TabEntry HostTab { get; set; }
    public void OnClose() => ViewModel.Dispose();
    public static void Open(TopLevel sender, JavaResourceDetailsTarget target, string title)
    {
        if (sender is not TioTabWindowBase window || string.IsNullOrWhiteSpace(target.ProjectId)) return;
        var tab = new TabEntry(window, new ModpackDetailsPage(target), title: title); window.CreateTab(tab); window.SelectTab(tab);
    }
}

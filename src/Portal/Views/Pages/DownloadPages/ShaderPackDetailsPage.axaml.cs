using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;

namespace Portal.Views.Pages.DownloadPages;

public partial class ShaderPackDetailsPage : UserControl, ITioTabPage
{
    public ShaderPackDetailsPage() : this(new JavaResourceDetailsTarget(JavaResourceDefinitions.ShaderPack, ModDetailsSource.Modrinth, string.Empty)) { }
    public ShaderPackDetailsPage(JavaResourceDetailsTarget target)
    {
        InitializeComponent(); ViewModel = new ShaderPackDetailsPageViewModel(target); DataContext = ViewModel;
        PageInfo = new PageInfo { Title = "光影包详情", Icon = StreamGeometry.Parse(JavaResourceDetailsIcon.Data) };
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }
    public ShaderPackDetailsPageViewModel ViewModel { get; }
    public PageInfo PageInfo { get; init; }
    public TabEntry HostTab { get; set; }
    public void OnClose() => ViewModel.Dispose();
    private async void VersionFile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: JavaResourceFileItem file } && TopLevel.GetTopLevel(this) is { } topLevel)
            await JavaResourceDownload.ShowInstallDialogAsync(topLevel, JavaResourceDefinitions.ShaderPack, file);
    }
    public static void Open(TopLevel sender, JavaResourceDetailsTarget target, string title)
    {
        if (sender is not TioTabWindowBase window || string.IsNullOrWhiteSpace(target.ProjectId)) return;
        var tab = new TabEntry(window, new ShaderPackDetailsPage(target), title: title); window.CreateTab(tab); window.SelectTab(tab);
    }
}

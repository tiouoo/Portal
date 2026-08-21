using Avalonia.Controls;
using Avalonia.Media;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

using Portal.Module;
namespace Portal.Views.Pages.DownloadPages;

public sealed class DownloadSearchTabPage : UserControl, ITioTabPage
{
    private readonly UserControl _page;
    private readonly object? _viewModel;

    public DownloadSearchTabPage(Type pageType, string keyword, string title)
    {
        _page = (UserControl)Activator.CreateInstance(pageType)!;
        _viewModel = _page.DataContext;
        if (_viewModel is ISearchPageViewModel searchPage)
            searchPage.SearchText = keyword;
        Content = _page;
        PageInfo.Title = title;
        PageInfo.Icon = GeometryResources.Get("PeopleGeometry");
    }

    public PageInfo PageInfo { get; init; } = new();
    public TabEntry HostTab { get; set; }

    public void OnClose()
    {
        if (_viewModel is IDisposable disposable)
            disposable.Dispose();
        _page.DataContext = null;
        Content = null;
    }
}
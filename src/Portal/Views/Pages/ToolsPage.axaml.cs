using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Module.AggregatedSearch;
using Portal.Module.AggregatedSearch;
using Portal.Module.DefaultPage;
using Portal.ViewModels;
using Portal.Views.Pages.ToolsPages;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Views.Pages;

[AggregatedSearchPage("实用工具", "实用工具", "Tools")]
[DefaultPage("实用工具")]
public partial class ToolsPage : DataUserControl, ITioTabPage
{
    public ToolsPage()
    {
        InitializeComponent();
        DataContext = new ToolsPageViewModel();
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = "实用工具",
        Icon = StreamGeometry.Parse(
            "F1 M640,640z M0,0z M256,320L216.5,359.5C203.9,354.6 190.3,352 176,352 114.1,352 64,402.1 64,464 64,525.9 114.1,576 176,576 237.9,576 288,525.9 288,464 288,449.7 285.3,436.1 280.5,423.5L563.2,140.8C570.3,133.7 570.3,122.3 563.2,115.2 534.9,86.9 489.1,86.9 460.8,115.2L320,256 280.5,216.5C285.4,203.9 288,190.3 288,176 288,114.1 237.9,64 176,64 114.1,64 64,114.1 64,176 64,237.9 114.1,288 176,288 190.3,288 203.9,285.3 216.5,280.5L256,320z M353.9,417.9L460.8,524.8C489.1,553.1 534.9,553.1 563.2,524.8 570.3,517.7 570.3,506.3 563.2,499.2L417.9,353.9 353.9,417.9z M128,176C128,149.5 149.5,128 176,128 202.5,128 224,149.5 224,176 224,202.5 202.5,224 176,224 149.5,224 128,202.5 128,176z M176,416C202.5,416 224,437.5 224,464 224,490.5 202.5,512 176,512 149.5,512 128,490.5 128,464 128,437.5 149.5,416 176,416z")
    };

    public TabEntry HostTab { get; set; }

    public void OnClose()
    {
        if (DataContext is IDisposable disposable) disposable.Dispose();
        DataContext = null;
    }
}

public partial class ToolsPageViewModel : ObservableObject, IDisposable
{
    private readonly Dictionary<Type, UserControl> _pageCache = new();

    [ObservableProperty] public partial UserControl? CurrentPage { get; set; }

    public ToolsPageViewModel()
    {
        if (IsWindows)
            NavigateType(typeof(BedrockToolsPage));
    }

    public bool IsWindows => OperatingSystem.IsWindows();

    [RelayCommand]
    private void NavigateType(object? parameter)
    {
        if (parameter is not Type pageType || !typeof(UserControl).IsAssignableFrom(pageType)) return;
        if (!_pageCache.TryGetValue(pageType, out var page) &&
            Activator.CreateInstance(pageType) is UserControl newPage)
            _pageCache[pageType] = page = newPage;
        CurrentPage = page;
    }

    public void Dispose()
    {
        CurrentPage = null;
        foreach (var page in _pageCache.Values)
            if (page is IDisposable disposable)
                disposable.Dispose();
        _pageCache.Clear();
    }
}
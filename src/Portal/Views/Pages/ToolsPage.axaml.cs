using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Module.AggregatedSearch;
using Portal.Module.DefaultPage;
using Portal.Localization;
using Portal.ViewModels;
using Portal.Views.Pages.ToolsPages;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

using Portal.Module;
namespace Portal.Views.Pages;

[AggregatedSearchPage("pages_tools", "pages_toolsPath", "Tools")]
[DefaultPage("pages_tools")]
public partial class ToolsPage : Dsc, ITioTabPage
{
    public ToolsPage()
    {
        InitializeComponent();
        DataContext = new ToolsPageViewModel();
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = CommonLanguageManager.Instance.toolsPage_pageTitle.CurrentValue(),
        IconGlyph = IconResources.GetGlyph("wrench"), IconFont = IconResources.FontFamilyName
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

    public ToolsPageViewModel()
    {
        if (IsWindows)
            NavigateType(typeof(BedrockToolsPage));
    }

    [ObservableProperty] public partial UserControl? CurrentPage { get; set; }

    public bool IsWindows => OperatingSystem.IsWindows();

    public void Dispose()
    {
        CurrentPage = null;
        foreach (var page in _pageCache.Values)
            if (page is IDisposable disposable)
                disposable.Dispose();
        _pageCache.Clear();
    }

    [RelayCommand]
    private void NavigateType(object? parameter)
    {
        if (parameter is not Type pageType || !typeof(UserControl).IsAssignableFrom(pageType)) return;
        if (!_pageCache.TryGetValue(pageType, out var page) &&
            Activator.CreateInstance(pageType) is UserControl newPage)
            _pageCache[pageType] = page = newPage;
        CurrentPage = page;
    }
}
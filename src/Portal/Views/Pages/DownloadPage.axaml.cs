using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Module.DefaultPage;
using Portal.Views.Pages.DownloadPages;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Views.Pages;

[DefaultPage("下载")]
public partial class DownloadPage : UserControl, ITioTabPage
{
    public DownloadPage()
    {
        InitializeComponent();
        DataContext = new DownloadPageViewModel();
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = "下载",
        Icon = StreamGeometry.Parse("F1 M640,640z M0,0z M544,269.8C529.2,279.6 512.2,287.5 494.5,293.8 447.5,310.6 385.8,320 320,320 254.2,320 192.4,310.5 145.5,293.8 127.9,287.5 110.8,279.6 96,269.8L96,352C96,396.2 196.3,432 320,432 443.7,432 544,396.2 544,352L544,269.8z M544,192L544,144C544,99.8 443.7,64 320,64 196.3,64 96,99.8 96,144L96,192C96,236.2 196.3,272 320,272 443.7,272 544,236.2 544,192z M494.5,453.8C447.6,470.5 385.9,480 320,480 254.1,480 192.4,470.5 145.5,453.8 127.9,447.5 110.8,439.6 96,429.8L96,496C96,540.2 196.3,576 320,576 443.7,576 544,540.2 544,496L544,429.8C529.2,439.6,512.2,447.5,494.5,453.8z")
    };

    public TabEntry HostTab { get; set; }
}

public partial class DownloadPageViewModel : ObservableObject
{
    [ObservableProperty] public partial UserControl? CurrentPage { get; set; }
    private readonly Dictionary<Type, UserControl> _pageCache = new();

    public DownloadPageViewModel()
    {
        NavigateType(typeof(VanillaInstallation));
    }

    [RelayCommand]
    public void NavigateType(object? parameter)
    {
        if (parameter is not Type pageType || !typeof(UserControl).IsAssignableFrom(pageType))
            return;

        if (!_pageCache.TryGetValue(pageType, out var page) &&
            Activator.CreateInstance(pageType) is UserControl newPage)
        {
            page = newPage;
            _pageCache[pageType] = page;
        }

        CurrentPage = page;
    }
}

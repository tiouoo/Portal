using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Bedrock.Standard.Interface;
using Portal.Core.Module.AggregatedSearch;
using Portal.Module.DefaultPage;
using Portal.Localization;
using Portal.Views.Pages.DownloadPages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

using Portal.Module;
namespace Portal.Views.Pages;

[AggregatedSearchPage("pages_download", "pages_downloadPath", "DownloadPage")]
[DefaultPage("pages_download")]
public partial class DownloadPage : UserControl, ITioTabPage
{
    public DownloadPageViewModel DownloadPageViewModel;

    public DownloadPage()
    {
        InitializeComponent();
        DownloadPageViewModel = new DownloadPageViewModel();
        DataContext = DownloadPageViewModel;
        Loaded += (s, e) =>
        {
            Logger.Info("[Download] Download page loaded.");
            var a = DownloadPageViewModel.CurrentPage;
            DownloadPageViewModel.CurrentPage = null;
            DownloadPageViewModel.CurrentPage = a;
        };
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = CommonLanguageManager.Instance.downloadPage_pageTitle.CurrentValue(),
        IconGlyph = "\ue653", IconFont = IconResources.FontFamilyName
    };

    public TabEntry HostTab { get; set; }

    public void OnClose()
    {
        Logger.Info("[Download] Download page closing.");
        DownloadPageViewModel.Dispose();
        DataContext = null;
    }
}

public partial class DownloadPageViewModel : ObservableObject, IDisposable
{
    private readonly Dictionary<Type, UserControl> _pageCache = new();

    public DownloadPageViewModel()
    {
        NavigateType(typeof(ModSearchPage));
    }

    [ObservableProperty] public partial UserControl? CurrentPage { get; set; }
    public bool IsBedrockInstallationSupported => BedrockInstallationService.DefaultInstaller is not null;

    public void Dispose()
    {
        Logger.Info($"[Download] Disposing {_pageCache.Count} cached download page(s).");
        CurrentPage = null;
        foreach (var page in _pageCache.Values)
        {
            if (page is IDisposable disposablePage)
                disposablePage.Dispose();
            else if (page.DataContext is IDisposable disposableViewModel)
                disposableViewModel.Dispose();
            page.DataContext = null;
        }

        _pageCache.Clear();
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
            Logger.Info($"[Download] Created download page {pageType.Name}.");
        }

        Logger.Info($"[Download] Navigating to {pageType.Name}.");
        CurrentPage = page;
    }
}
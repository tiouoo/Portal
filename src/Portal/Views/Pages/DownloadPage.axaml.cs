using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Bedrock.Standard.Interface;
using Portal.Core.Const;
using Portal.Core.Module.AggregatedSearch;
using Portal.Module.DefaultPage;
using Portal.Localization;
using Portal.Views.Pages.DownloadPages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Controls;

using Portal.Module;
using Portal.ViewModels;

namespace Portal.Views.Pages;

[AggregatedSearchPage("pages_download", "pages_downloadPath", "DownloadPage")]
[DefaultPage("pages_download")]
public partial class DownloadPage : Dsc, ITioTabPage
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
            RestoreNavState();
            var a = DownloadPageViewModel.CurrentPage;
            DownloadPageViewModel.CurrentPage = null;
            DownloadPageViewModel.CurrentPage = a;
        };
    }

    private void RestoreNavState()
    {
        var navMenu = this.FindControl<NavMenu>("NavMenu");
        if (navMenu is null) return;

        WireNavPersistence(navMenu);

        var saved = Data.ConfigEntry.DownloadLastSelectedPage;
        var leaves = GetLeafItems(navMenu).ToList();
        var target = leaves.FirstOrDefault(item => (item.CommandParameter as Type)?.Name == saved);
        target ??= leaves.FirstOrDefault(item => (item.CommandParameter as Type) == typeof(ModSearchPage));
        target ??= leaves.FirstOrDefault();
        if (target?.CommandParameter is Type pageType)
            DownloadPageViewModel.NavigateType(pageType);
        if (target is not null)
            navMenu.SelectedItem = target;
    }

    private void WireNavPersistence(NavMenu navMenu)
    {
        WireCollapsePersistence("JavaEditionNavItem", value => Data.ConfigEntry.DownloadJavaEditionExpanded = !value);
        WireCollapsePersistence("BedrockEditionNavItem", value => Data.ConfigEntry.DownloadBedrockEditionExpanded = !value);
        WireCollapsePersistence("OthersNavItem", value => Data.ConfigEntry.DownloadOthersExpanded = !value);
    }

    private void WireCollapsePersistence(string itemName, Action<bool> setter)
    {
        if (this.FindControl<NavMenuItem>(itemName) is not { } item) return;
        item.PropertyChanged += (_, e) =>
        {
            if (e.Property == NavMenuItem.IsVerticalCollapsedProperty)
                setter(item.IsVerticalCollapsed);
        };
    }

    private static IEnumerable<NavMenuItem> GetLeafItems(NavMenu navMenu)
    {
        foreach (var topItem in navMenu.Items.OfType<NavMenuItem>())
        foreach (var childItem in topItem.Items.OfType<NavMenuItem>())
            yield return childItem;
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
    
    public Data Data => Data.Instance;

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

        Data.ConfigEntry.DownloadLastSelectedPage = pageType.Name;

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
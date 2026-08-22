using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Module.DefaultPage;
using Portal.Core.Const;
using Portal.Localization;
using Portal.Views.Pages.SettingPages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Controls;

using Portal.Module;
namespace Portal.Views.Pages;

[DefaultPage("pages_settings")]
public partial class SettingPage : UserControl, ITioTabPage
{
    public SettingPageViewModel SettingPageViewModel;

    public SettingPage()
    {
        InitializeComponent();
        SettingPageViewModel = new SettingPageViewModel();
        DataContext = SettingPageViewModel;
        Loaded += (s, e) =>
        {
            Logger.Info("[Settings] Settings page loaded.");
            WireNavPersistence();
            var a = SettingPageViewModel.CurrentPage;
            SettingPageViewModel.CurrentPage = null;
            SettingPageViewModel.CurrentPage = a;
        };
    }

    private void WireNavPersistence()
    {
        WireCollapsePersistence("GeneralNavItem", value => Data.ConfigEntry.SettingsNavGeneralExpanded = !value);
        WireCollapsePersistence("GameNavItem", value => Data.ConfigEntry.SettingsNavGameExpanded = !value);
        WireCollapsePersistence("NetworkNavItem", value => Data.ConfigEntry.SettingsNavNetworkExpanded = !value);
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

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = CommonLanguageManager.Instance.settingPage_pageTitle.CurrentValue(),
        IconGlyph = "\ue627", IconFont = IconResources.FontFamilyName
    };

    public TabEntry HostTab { get; set; }

    public void OnClose()
    {
        Logger.Info("[Settings] Settings page closing; disposing cached setting pages.");
        SettingPageViewModel.Dispose();
        DataContext = null;
    }

    public void NavigateTo(Type pageType)
    {
        Logger.Info($"[Settings] Navigating to {pageType.Name}.");
        SettingPageViewModel.NavigateType(pageType);
        SelectNavMenuItem(pageType);
    }

    private void SelectNavMenuItem(Type pageType)
    {
        var navMenu = this.FindControl<NavMenu>("NavMenu");
        if (navMenu == null) return;

        foreach (var topItem in navMenu.Items.OfType<NavMenuItem>())
        foreach (var childItem in topItem.Items.OfType<NavMenuItem>())
            if (childItem.CommandParameter is Type paramType && paramType == pageType)
            {
                navMenu.SelectedItem = childItem;
                return;
            }
    }
}

public partial class SettingPageViewModel : ObservableObject, IDisposable
{
    private readonly Dictionary<Type, UserControl> _settingPageCache = new();

    public Data Data => Data.Instance;

    public SettingPageViewModel()
    {
        NavigateType(typeof(Appearance));
    }

    [ObservableProperty] public partial UserControl? CurrentPage { get; set; }

    public void Dispose()
    {
        Logger.Info($"[Settings] Disposing {_settingPageCache.Count} cached setting page(s).");
        CurrentPage = null;
        foreach (var page in _settingPageCache.Values.OfType<IDisposable>())
            page.Dispose();
        _settingPageCache.Clear();
    }

    [RelayCommand]
    public void NavigateType(object? parameter)
    {
        if (parameter is not Type pageType) return;

        if (!typeof(UserControl).IsAssignableFrom(pageType)) return;

        if (!_settingPageCache.TryGetValue(pageType, out var page))
            if (Activator.CreateInstance(pageType) is UserControl newPage)
            {
                page = newPage;
                _settingPageCache[pageType] = page;
                Logger.Info($"[Settings] Created settings page {pageType.Name}.");
            }

        if (page != null) CurrentPage = page;
    }
}
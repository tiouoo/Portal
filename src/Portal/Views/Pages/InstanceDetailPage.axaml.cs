using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft.Classes;
using Portal.Services;
using Portal.Views.Pages.InstancePages;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Controls;

using Portal.Module;
namespace Portal.Views.Pages;

public partial class InstanceDetailPage : UserControl, ITioTabPage
{
    public InstanceDetailPage(MinecraftInstance instance)
    {
        InitializeComponent();
        ViewModel = new InstanceDetailPageViewModel(instance, this);
        DataContext = ViewModel;
        PageInfo = new PageInfo
        {
            Title = instance.InstanceName,
            Icon = GeometryResources.Get("DocumentLinesGeometry")
        };
        instance.PropertyChanged += Instance_PropertyChanged;
        Loaded += (s, e) =>
        {
            var a = ViewModel.CurrentPage;
            ViewModel.CurrentPage = null;
            ViewModel.CurrentPage = a;
        };
    }

    public InstanceDetailPage()
    {
    }

    public InstanceDetailPageViewModel ViewModel { get; }

    public PageInfo PageInfo { get; init; }

    public TabEntry HostTab { get; set; }

    public void OnClose()
    {
        ViewModel.ClosePages();
        ViewModel.Instance.PropertyChanged -= Instance_PropertyChanged;
    }

    public Task<bool> RequestCloseAsync()
    {
        return ViewModel.RequestCloseAsync();
    }

    private void Instance_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MinecraftInstance.InstanceName))
            return;

        PageInfo.Title = ViewModel.Instance.InstanceName;
        if (HostTab != null)
            HostTab.Title = PageInfo.Title;
    }

    public static void Open(MinecraftInstance instance, TopLevel sender)
    {
        if (InstanceDeletionCoordinator.IsDeleting(instance))
            return;

        if (sender is not TioTabWindowBase window)
            return;

        var tab = new TabEntry(window, new InstanceDetailPage(instance));
        window.CreateTab(tab);
        window.SelectTab(tab);
    }

    public void NavigateTo(Type pageType)
    {
        if (pageType == null) return;
        ViewModel.NavigateType(pageType);
        var navMenu = this.FindControl<NavMenu>("NavMenu");
        var item = navMenu?.Items.OfType<NavMenuItem>()
            .FirstOrDefault(x => x.CommandParameter is Type type && type == pageType);
        if (item != null)
            navMenu!.SelectedItem = item;
    }
}

public partial class InstanceDetailPageViewModel : ObservableObject
{
    private readonly Dictionary<Type, UserControl> _pageCache = new();
    private readonly InstanceDetailPage _parent;

    public InstanceDetailPageViewModel(MinecraftInstance instance, InstanceDetailPage parent)
    {
        Instance = instance;
        _parent = parent;
        NavigateType(typeof(Dashboard));
    }

    public MinecraftInstance Instance { get; }

    [ObservableProperty] public partial UserControl? CurrentPage { get; set; }

    public bool IsJava => Instance.IsJava;
    public bool IsBedrock => Instance.IsBedrock;
    public bool SupportsBedrockMods => OperatingSystem.IsWindows() && Instance.IsBedrock;
    public bool IsGdkBedrock => SupportsBedrockMods;

    [RelayCommand]
    public void NavigateType(object? parameter)
    {
        if (parameter is not Type pageType || !typeof(UserControl).IsAssignableFrom(pageType))
            return;

        var constructor = pageType.GetConstructor([typeof(MinecraftInstance), typeof(InstanceDetailPage)])
                          ?? pageType.GetConstructor([typeof(MinecraftInstance)]);
        var arguments = constructor?.GetParameters().Length == 2
            ? new object[] { Instance, _parent }
            : new object[] { Instance };

        if (!_pageCache.TryGetValue(pageType, out var page) &&
            constructor?.Invoke(arguments) is UserControl newPage)
        {
            page = newPage;
            _pageCache[pageType] = page;
        }

        if (page != null)
            CurrentPage = page;
    }

    public Task<bool> RequestCloseAsync()
    {
        return _pageCache.TryGetValue(typeof(ConfigFiles), out var page) && page is ConfigFiles configFiles
            ? configFiles.RequestCloseAsync()
            : Task.FromResult(true);
    }

    public void ClosePages()
    {
        foreach (var page in _pageCache.Values.OfType<IDisposable>())
            page.Dispose();
        _pageCache.Clear();
        CurrentPage = null;
    }
}
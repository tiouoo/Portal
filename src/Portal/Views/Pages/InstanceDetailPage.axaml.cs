using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft.Classes;
using Portal.Views.Pages.InstancePages;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Gateway;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Controls;

namespace Portal.Views.Pages;

public partial class InstanceDetailPage : UserControl, ITioTabPage
{
    public InstanceDetailPageViewModel ViewModel { get; }

    public InstanceDetailPage(MinecraftInstance instance)
    {
        InitializeComponent();
        ViewModel = new InstanceDetailPageViewModel(instance);
        DataContext = ViewModel;
        PageInfo = new PageInfo
        {
            Title = instance.InstanceName,
            Icon = StreamGeometry.Parse(
                "F1 M640,640z M0,0z M480,576L192,576C139,576,96,533,96,480L96,160C96,107,139,64,192,64L496,64C522.5,64,544,85.5,544,112L544,400C544,420.9,530.6,438.7,512,445.3L512,512C529.7,512 544,526.3 544,544 544,561.7 529.7,576 512,576L480,576z M192,448C174.3,448 160,462.3 160,480 160,497.7 174.3,512 192,512L448,512 448,448 192,448z M224,216C224,229.3,234.7,240,248,240L424,240C437.3,240 448,229.3 448,216 448,202.7 437.3,192 424,192L248,192C234.7,192,224,202.7,224,216z M248,288C234.7,288 224,298.7 224,312 224,325.3 234.7,336 248,336L424,336C437.3,336 448,325.3 448,312 448,298.7 437.3,288 424,288L248,288z")
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


    public PageInfo PageInfo { get; init; }

    public TabEntry HostTab { get; set; }

    private void Instance_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MinecraftInstance.InstanceName))
            return;

        PageInfo.Title = ViewModel.Instance.InstanceName;
        if (HostTab != null)
            HostTab.Title = PageInfo.Title;
    }

    public void OnClose()
    {
        ViewModel.Instance.PropertyChanged -= Instance_PropertyChanged;
    }

    public static void Open(MinecraftInstance instance, TopLevel sender)
    {
        if (sender is not TioTabWindowBase window)
            return;

        var tab = new TabEntry(window, new InstanceDetailPage(instance));
        window.CreateTab(tab);
        window.SelectTab(tab);
    }

    public void NavigateTo(Type pageType)
    {
        ViewModel.NavigateType(pageType);
        var navMenu = this.FindControl<NavMenu>("NavMenu");
        var item = navMenu?.Items.OfType<NavMenuItem>()
            .SelectMany(x => x.Items.OfType<NavMenuItem>())
            .FirstOrDefault(x => x.CommandParameter is Type type && type == pageType);
        if (item != null)
            navMenu!.SelectedItem = item;
    }
}

public partial class InstanceDetailPageViewModel : ObservableObject
{
    public MinecraftInstance Instance { get; }

    [ObservableProperty] public partial UserControl? CurrentPage { get; set; }

    private readonly Dictionary<Type, UserControl> _pageCache = new();

    public InstanceDetailPageViewModel(MinecraftInstance instance)
    {
        Instance = instance;
        NavigateType(typeof(Dashboard));
    }

    [RelayCommand]
    public void NavigateType(object? parameter)
    {
        if (parameter is not Type pageType || !typeof(UserControl).IsAssignableFrom(pageType))
            return;

        if (!_pageCache.TryGetValue(pageType, out var page) &&
            Activator.CreateInstance(pageType, Instance) is UserControl newPage)
        {
            page = newPage;
            _pageCache[pageType] = page;
        }

        if (page != null)
            CurrentPage = page;
    }
}

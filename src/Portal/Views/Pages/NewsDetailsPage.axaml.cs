using Avalonia.Controls;
using Avalonia.Media;
using Portal.Core.Minecraft.Classes;
using Portal.Localization;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

using Portal.Module;
namespace Portal.Views.Pages;

public partial class NewsDetailsPage : UserControl, ITioTabPage
{
    public NewsDetailsPage() : this(new NewsEntry
    {
        Title = CommonLanguageManager.Instance.newsDetails_pageTitle.CurrentValue(),
        ShortText = string.Empty
    })
    {
    }

    public NewsDetailsPage(NewsEntry entry)
    {
        InitializeComponent();
        ViewModel = new NewsDetailsPageViewModel(entry);
        DataContext = ViewModel;
        PageInfo = new PageInfo
        {
            Title = string.IsNullOrEmpty(entry.Title)
                ? CommonLanguageManager.Instance.newsDetails_pageTitle.CurrentValue()
                : entry.Title,
            IconGlyph = IconResources.GetGlyph("book"), IconFont = IconResources.FontFamilyName
        };
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    public NewsDetailsPageViewModel ViewModel { get; }

    public PageInfo PageInfo { get; init; }
    public TabEntry HostTab { get; set; }

    public void OnClose()
    {
        ViewModel.Dispose();
        DataContext = null;
    }

    public static void Open(TopLevel sender, NewsEntry entry)
    {
        if (sender is not TioTabWindowBase window) return;
        if (entry == null || string.IsNullOrEmpty(entry.Id)) return;
        var tab = new TabEntry(window, new NewsDetailsPage(entry));
        window.CreateTab(tab);
        window.SelectTab(tab);
    }
}
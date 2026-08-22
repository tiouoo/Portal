using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Module.AggregatedSearch;
using Portal.Module.DefaultPage;
using Portal.Localization;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Controls;

using Portal.Module;
namespace Portal.Views.Pages;

[AggregatedSearchPage("pages_news", "pages_newsPath", "News")]
[DefaultPage("pages_news")]
public partial class NewsPage : Dsc, ITioTabPage
{
    public NewsPage(bool isInset = false)
    {
        InitializeComponent();

        NewsPageViewModel = NewsPageViewModel.Instance;
        DataContext = NewsPageViewModel;

        if (!isInset)
        {
            Margin = new Thickness(10, 0, 10, 10);
            ScrollView.VerticalScrollMode = ScrollMode.Enabled;
            Button.Margin = new Thickness(15, 2, 0, 0);
        }
    }

    public NewsPage() : this(false)
    {
    }

    public NewsPageViewModel NewsPageViewModel { get; }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = CommonLanguageManager.Instance.news_pageTitle.CurrentValue(),
        IconGlyph = "\ue63d", IconFont = IconResources.FontFamilyName
    };

    public TabEntry HostTab { get; set; }


    public void OnClose()
    {
        DataContext = null;
    }

    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is null || sender.AsTopLevel() is not TioTabWindowBase window)
            return;

        var tab = new TabEntry(window, new NewsPage());
        window.CreateTab(tab);
        window.SelectTab(tab);
    }

    private void NewsCard_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Control { DataContext: NewsEntry entry } control) return;

        if (e.InitialPressMouseButton != MouseButton.Left) return;
        NewsDetailsPage.Open(control.AsTopLevel(), entry);
    }
}
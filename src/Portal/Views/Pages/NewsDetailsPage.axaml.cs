using Avalonia.Controls;
using Avalonia.Media;
using Portal.Core.Minecraft.Classes;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Views.Pages;

/// <summary>
/// 新闻详情页。接收一个 <see cref="NewsEntry"/>，按需拉取正文并缓存。
/// 通过 <see cref="Open"/> 在新标签页打开。
/// </summary>
public partial class NewsDetailsPage : UserControl, ITioTabPage
{
    public NewsDetailsPageViewModel ViewModel { get; }

    // 无参构造供 XAML 预览使用。
    public NewsDetailsPage() : this(new NewsEntry
    {
        Title = "新闻详情",
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
            Title = string.IsNullOrEmpty(entry.Title) ? "新闻详情" : entry.Title,
            Icon = StreamGeometry.Parse(
                "F1 M640,640z M0,0z M128,96C128,78 142,64 160,64L480,64C498,64 512,78 512,96L512,544C512,562 498,576 480,576L160,576C142,576 128,562 128,544L128,96z M192,160L192,192H448V160H192z M192,256V288H448V256H192z M192,352V384H352V352H192z")
        };
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    public PageInfo PageInfo { get; init; }
    public TabEntry HostTab { get; set; }

    public void OnClose()
    {
        ViewModel.Dispose();
        DataContext = null;
    }

    /// <summary>
    /// 在指定顶级窗口内打开新闻详情页（新标签页）。点击新闻卡片 / 小组件时调用。
    /// </summary>
    public static void Open(TopLevel sender, NewsEntry entry)
    {
        if (sender is not TioTabWindowBase window) return;
        if (entry == null || string.IsNullOrEmpty(entry.Id)) return;
        var tab = new TabEntry(window, new NewsDetailsPage(entry));
        window.CreateTab(tab);
        window.SelectTab(tab);
    }
}

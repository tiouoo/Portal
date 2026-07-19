using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Portal.Module.DefaultPage;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Views.Pages;

[DefaultPage("下载")]
public partial class DownloadPage : UserControl, ITioTabPage
{
    public DownloadPage()
    {
        InitializeComponent();
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = "下载",
        Icon = StreamGeometry.Parse("F1 M640,640z M0,0z M176,544C96.5,544 32,479.5 32,400 32,336.6 73,282.8 129.9,263.5 128.6,255.8 128,248 128,240 128,160.5 192.5,96 272,96 327.4,96 375.5,127.3 399.6,173.1 413.8,164.8 430.4,160 448,160 501,160 544,203 544,256 544,271.7 540.2,286.6 533.5,299.7 577.5,320 608,364.4 608,416 608,486.7 550.7,544 480,544L176,544z M409,377C418.4,367.6 418.4,352.4 409,343.1 399.6,333.8 384.4,333.7 375.1,343.1L344.1,374.1 344.1,272C344.1,258.7 333.4,248 320.1,248 306.8,248 296.1,258.7 296.1,272L296.1,374.1 265.1,343.1C255.7,333.7 240.5,333.7 231.2,343.1 221.9,352.5 221.8,367.7 231.2,377L303.2,449C312.6,458.4,327.8,458.4,337.1,449L409.1,377z")
    };

    public TabEntry HostTab { get; set; }
}

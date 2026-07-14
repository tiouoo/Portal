using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Portal.Module.AggregatedSearch;
using Portal.ViewModels;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("下载", "设置/下载", "Download")]
public partial class Download : DataUserControl
{
    public Download()
    {
        InitializeComponent();
        DataContext = this;
    }
}
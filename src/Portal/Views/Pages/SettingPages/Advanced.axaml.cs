using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Portal.Module.AggregatedSearch;
using Portal.ViewModels;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("高级", "设置/高级", "Advanced")]
public partial class Advanced : DataUserControl
{
    public Advanced()
    {
        InitializeComponent();
        DataContext = this;
    }
}
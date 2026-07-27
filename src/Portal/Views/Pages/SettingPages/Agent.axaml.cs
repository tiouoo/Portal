using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Portal.Module.AggregatedSearch;
using Portal.ViewModels;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("网络代理", "设置/网络代理", "Agent")]
public partial class Agent : DataUserControl
{
    public Agent()
    {
        InitializeComponent();
        DataContext = this;
    }

    public object DefaultAgent => $"Portal/{Data.Version.VersionTitle}";
}
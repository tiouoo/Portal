using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Portal.Core.App.Service;
using Portal.Core.Module.AggregatedSearch;
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

    public object DefaultAgent => $"Portal/{AppVersionService.Instance.Version.VersionTitle}";
}
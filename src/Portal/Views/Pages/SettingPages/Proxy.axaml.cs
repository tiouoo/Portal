using Portal.Core.Module.AggregatedSearch;
using Portal.Core.Services;
using Portal.ViewModels;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("pages_proxy", "pages_proxyPath", "Proxy")]
public partial class Proxy : Dsc
{
    public Proxy()
    {
        InitializeComponent();
        DataContext = this;
    }

    public object DefaultAgent => $"Portal/{AppVersionService.Instance.Version.VersionTitle}";
}
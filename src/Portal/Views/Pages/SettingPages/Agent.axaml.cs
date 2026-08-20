using Portal.Core.Module.AggregatedSearch;
using Portal.Core.Services;
using Portal.ViewModels;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("Proxy", "Settings/Proxy", "Agent")]
public partial class Agent : Dsc
{
    public Agent()
    {
        InitializeComponent();
        DataContext = this;
    }

    public object DefaultAgent => $"Portal/{AppVersionService.Instance.Version.VersionTitle}";
}
using System.Runtime.InteropServices;
using Portal.Core.Module.AggregatedSearch;
using Portal.ViewModels;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("Advanced", "Settings/Advanced", "Advanced")]
public partial class Advanced : Dsc
{
    public Advanced()
    {
        InitializeComponent();
        DataContext = this;
    }

    public bool IsOverlaySupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
}
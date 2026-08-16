using System.Runtime.InteropServices;
using Portal.Core.Module.AggregatedSearch;
using Portal.ViewModels;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("高级选项", "设置/高级选项", "Advanced")]
public partial class Advanced : DataUserControl
{
    public Advanced()
    {
        InitializeComponent();
        DataContext = this;
    }

    public bool IsOverlaySupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
}
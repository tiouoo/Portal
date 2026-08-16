using Avalonia.Controls;
using Portal.Core.Module.AggregatedSearch;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Extensions;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("通知选项", "设置/通知选项", "Notification")]
public partial class Notification : DataUserControl
{
    public Notification()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void SelectingItemsControl_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        (sender as Control)!.GetTopLevel().Notice("通知测试");
    }
}
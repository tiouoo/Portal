using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Portal.Const;
using Portal.Module.AggregatedSearch;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Extensions;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("通知", "设置/通知", "Notification")]
public partial class Notification : DataUserControl
{
    public Notification()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void SelectingItemsControl_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        NotificationGateway.Notice((sender as Control)!.GetTopLevel(), "通知测试");
    }
}
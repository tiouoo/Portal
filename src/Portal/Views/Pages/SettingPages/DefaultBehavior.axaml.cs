using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Portal.Module.AggregatedSearch;
using Portal.ViewModels;
using TioUi.Common.Extensions;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("默认行为", "设置/默认行为", "DefaultBehavior")]
public partial class DefaultBehavior : DataUserControl
{
    public DefaultBehavior()
    {
        InitializeComponent();
        DataContext = this;
    }
}
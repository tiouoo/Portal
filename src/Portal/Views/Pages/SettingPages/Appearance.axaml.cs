using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Portal.Module.AggregatedSearch;
using Portal.ViewModels;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("显示与外观", "设置/显示与外观", "Appearance")]
public partial class Appearance : DataUserControl
{
    public Appearance()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => { ListBox.SelectedIndex = (int)Const.Data.ConfigEntry.Theme; };
        ListBox.SelectionChanged += (_, _) =>
        {
            if (ListBox.SelectedIndex == -1) return;
            Const.Data.ConfigEntry.Theme = (TioUi.Shared.Theme)ListBox.SelectedIndex;
        };
    }
}
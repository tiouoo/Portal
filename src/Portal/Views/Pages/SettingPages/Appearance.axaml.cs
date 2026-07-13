using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Portal.ViewModels;

namespace Portal.Views.Pages.SettingPages;

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
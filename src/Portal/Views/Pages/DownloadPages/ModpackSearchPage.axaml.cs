using Avalonia.Controls;
using Avalonia.Input;

namespace Portal.Views.Pages.DownloadPages;

public partial class ModpackSearchPage : UserControl
{
    public ModpackSearchPage()
    {
        InitializeComponent();
        DataContext = new ModpackSearchPageViewModel();
        Loaded += async (_, _) => await ((ModpackSearchPageViewModel)DataContext).InitializeAsync();
    }

    private void SearchBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ModpackSearchPageViewModel viewModel) return;
        viewModel.SearchCommand.Execute(null);
        e.Handled = true;
    }

    private void Result_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Control).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed ||
            (sender as Control)?.DataContext is not JavaResourceSearchResultItem item ||
            TopLevel.GetTopLevel(this) is not { } topLevel) return;
        ModpackDetailsPage.Open(topLevel, item.Target, item.Name);
        e.Handled = true;
    }
}

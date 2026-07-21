using Avalonia.Controls;
using Avalonia.Input;

namespace Portal.Views.Pages.DownloadPages;

public partial class SaveSearchPage : UserControl
{
    public SaveSearchPage()
    {
        InitializeComponent(); DataContext = new SaveSearchPageViewModel();
        Loaded += async (_, _) => await ((SaveSearchPageViewModel)DataContext).InitializeAsync();
    }

    private void SearchBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not SaveSearchPageViewModel viewModel) return;
        viewModel.SearchCommand.Execute(null); e.Handled = true;
    }

    private void Result_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Control).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed ||
            (sender as Control)?.DataContext is not JavaResourceSearchResultItem item || TopLevel.GetTopLevel(this) is not { } topLevel) return;
        SaveDetailsPage.Open(topLevel, item.Target, item.Name); e.Handled = true;
    }
}

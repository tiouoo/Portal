using Avalonia.Controls;
using Avalonia.Input;
using Portal.Services;

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
    private void Favorite_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is JavaResourceSearchResultItem item)
        {
            var resource = FavoriteResourceFactory.From(item, FavoriteEdition.Java);
            if (item.IsFavorite) FavoriteCollectionService.Instance.Remove(resource);
            else FavoriteCollectionService.Instance.Add(resource);
            item.IsFavorite = !item.IsFavorite;
        }
        e.Handled = true;
    }
    private void Download_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is JavaResourceSearchResultItem item && TopLevel.GetTopLevel(this) is { } topLevel)
            ModpackDetailsPage.Open(topLevel, item.Target, item.Name);
        e.Handled = true;
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Portal.Core.Services;

namespace Portal.Views.Pages.DownloadPages;

public abstract class ResourceSearchPageBase : UserControl
{
    protected void SearchBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ISearchPageViewModel viewModel) return;
        viewModel.ExecuteSearch();
        e.Handled = true;
    }

    protected void Result_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Control).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed ||
            (sender as Control)?.DataContext is not JavaResourceSearchResultItem item ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        OpenDetails(topLevel, item);
        e.Handled = true;
    }

    protected void Favorite_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is JavaResourceSearchResultItem item)
        {
            var resource = FavoriteResourceFactory.From(item, FavoriteEdition);
            if (item.IsFavorite) FavoriteCollectionService.Instance.Remove(resource);
            else FavoriteCollectionService.Instance.Add(resource);
            item.IsFavorite = !item.IsFavorite;
        }

        e.Handled = true;
    }

    protected async void Download_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is JavaResourceSearchResultItem item && TopLevel.GetTopLevel(this) is { } topLevel)
            await QuickDownloadAsync(topLevel, item);
        e.Handled = true;
    }

    protected virtual FavoriteEdition FavoriteEdition => FavoriteEdition.Java;

    protected virtual void OpenDetails(TopLevel topLevel, JavaResourceSearchResultItem item)
    {
        JavaResourceDetailsPage.Open(topLevel, item.Target, item.Name);
    }

    protected virtual Task QuickDownloadAsync(TopLevel topLevel, JavaResourceSearchResultItem item)
    {
        return JavaResourceDownload.QuickDownloadAsync(topLevel, item.Target);
    }
}

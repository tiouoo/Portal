using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Portal.Core.Services;

namespace Portal.Views.Pages.DownloadPages;

public partial class ShaderPackSearchPage : UserControl
{
    public ShaderPackSearchPage()
    {
        InitializeComponent();
        DataContext = new ShaderPackSearchPageViewModel();
        Loaded += async (_, _) => await ((ShaderPackSearchPageViewModel)DataContext).InitializeAsync();
    }

    private void SearchBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ShaderPackSearchPageViewModel viewModel) return;
        viewModel.SearchCommand.Execute(null);
        e.Handled = true;
    }

    private void Result_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Control).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed ||
            (sender as Control)?.DataContext is not JavaResourceSearchResultItem item ||
            TopLevel.GetTopLevel(this) is not { } topLevel) return;
        ShaderPackDetailsPage.Open(topLevel, item.Target, item.Name);
        e.Handled = true;
    }

    private void Favorite_OnClick(object? sender, RoutedEventArgs e)
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

    private async void Download_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is JavaResourceSearchResultItem item && TopLevel.GetTopLevel(this) is { } topLevel)
            await JavaResourceDownload.QuickDownloadAsync(topLevel, item.Target);
        e.Handled = true;
    }
}
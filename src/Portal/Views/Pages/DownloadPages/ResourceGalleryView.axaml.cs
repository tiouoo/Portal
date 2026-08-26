using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Portal.Module.Imaging;
using Portal.Views.Pages.StaticPages;

namespace Portal.Views.Pages.DownloadPages;

public partial class ResourceGalleryView : UserControl
{
    private Bitmap? _copiedBitmap;

    public ResourceGalleryView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) =>
        {
            _copiedBitmap?.Dispose();
            _copiedBitmap = null;
        };
    }

    private void OpenImageViewer_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            sender is not Control { DataContext: ResourceScreenshot item } ||
            DataContext is not ResourceDetailsViewModel viewModel ||
            TopLevel.GetTopLevel(this) is not { } topLevel ||
            viewModel.Screenshots.Count == 0)
            return;

        var currentIndex = viewModel.Screenshots.IndexOf(item);
        if (currentIndex < 0)
            return;

        ImageViewer.Open(
            viewModel.Screenshots
                .Select(screenshot => new ImageViewerItem(screenshot.FullUrl ?? screenshot.Url, screenshot.Name))
                .ToArray(),
            currentIndex, topLevel);
        e.Handled = true;
    }

    private static async Task<string?> DownloadAsync(ResourceScreenshot item)
    {
        return await RemoteImageHelper.EnsureLocalAsync(item.FullUrl ?? item.Url);
    }

    private async void OpenScreenshot_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not ResourceScreenshot item)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        if (Uri.TryCreate(item.FullUrl ?? item.Url, UriKind.Absolute, out var uri))
            await topLevel.Launcher.LaunchUriAsync(uri);
    }

    private async void CopyScreenshot_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not ResourceScreenshot item)
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
            return;

        var path = await DownloadAsync(item);
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = new Bitmap(stream);
            _copiedBitmap?.Dispose();
            _copiedBitmap = bitmap;
            await clipboard.SetBitmapAsync(bitmap);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (ArgumentException)
        {
        }
    }

    private async void SaveScreenshotAs_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not ResourceScreenshot item)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var path = await DownloadAsync(item);
        if (string.IsNullOrEmpty(path))
            return;

        var destination = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(item.Name) + ".png",
            FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }]
        });
        if (destination == null)
            return;

        try
        {
            await using var source = File.OpenRead(path);
            await using var target = await destination.OpenWriteAsync();
            await source.CopyToAsync(target);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
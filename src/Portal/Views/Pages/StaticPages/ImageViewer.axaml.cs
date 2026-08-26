using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

using Portal.Localization;
using Portal.Module;
using Portal.Module.Imaging;
namespace Portal.Views.Pages.StaticPages;

public partial class ImageViewer : UserControl, ITioTabPage, IContextMenuTabPage, INotifyPropertyChanged, IDisposable
{
    private readonly IReadOnlyList<ImageViewerItem> _items;
    private bool _isDisposed;
    private Bitmap? _image;
    private int _currentIndex;
    private int _loadVersion;

    public ImageViewer() : this(string.Empty, (IReadOnlyList<ImageViewerItem>?)null)
    {
    }

    public ImageViewer(string filePath) : this(filePath, (IReadOnlyList<ImageViewerItem>?)null)
    {
    }

    public ImageViewer(string filePath, IReadOnlyList<string>? filePaths)
        : this(filePath, filePaths?.Select(path => new ImageViewerItem(path, Path.GetFileName(path))).ToArray())
    {
    }

    public ImageViewer(string source, IReadOnlyList<ImageViewerItem>? items)
    {
        FilePath = source;
        _items = items is { Count: > 0 } ? items : [new ImageViewerItem(source, Path.GetFileName(source))];
        _currentIndex = -1;
        for (var i = 0; i < _items.Count; i++)
        {
            if (string.Equals(_items[i].Source, source, StringComparison.OrdinalIgnoreCase))
            {
                _currentIndex = i;
                break;
            }
        }

        FileName = _currentIndex >= 0 ? _items[_currentIndex].DisplayName : Path.GetFileName(source);

        PageInfo = new PageInfo
        {
            Title = FileName,
            IconGlyph = "\ue632", IconFont = IconResources.FontFamilyName
        };

        InitializeComponent();
        DataContext = this;

        _ = LoadImageAsync();
    }

    public string FilePath { get; private set; }
    public string FileName { get; private set; }

    public bool CanNavigatePrevious => _currentIndex > 0;

    public bool CanNavigateNext => _currentIndex >= 0 && _currentIndex < _items.Count - 1;

    public Bitmap? Image
    {
        get => _image;
        private set
        {
            if (_image == value)
                return;
            _image = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
        }
    }

    private async Task LoadImageAsync()
    {
        if (_isDisposed || string.IsNullOrWhiteSpace(FilePath))
            return;

        var version = ++_loadVersion;
        Bitmap? bitmap;
        try
        {
            var source = FilePath;
            bitmap = await Task.Run(async () =>
            {
                var localPath = source;
                if (IsRemote(source))
                {
                    localPath = await RemoteImageHelper.EnsureLocalAsync(source);
                    if (string.IsNullOrEmpty(localPath))
                        return null;
                }

                return File.Exists(localPath) ? new Bitmap(localPath) : null;
            });
        }
        catch (Exception)
        {
            return;
        }

        if (bitmap == null || _isDisposed || version != _loadVersion)
        {
            bitmap?.Dispose();
            return;
        }

        Image = bitmap;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        Image?.Dispose();
    }

    public PageInfo PageInfo { get; init; }
    public TabEntry HostTab { get; set; }

    public void OnClose()
    {
        DataContext = null;
        Dispatcher.UIThread.Post(Dispose, DispatcherPriority.Background);
    }

    public void BuildContextMenu(IList<MenuItem> menuItems)
    {
        menuItems.Add(new MenuItem
        {
            Header = IsRemote(FilePath)
                ? StaticPagesLanguageManager.Instance.imageviewer_openInBrowser.CurrentValue()
                : StaticPagesLanguageManager.Instance.imageviewer_openWithDefault.CurrentValue(),
            Icon = CreateMenuItemIcon("\ue628"),
            Command = new RelayCommand(async () => await OpenWithDefaultAsync())
        });

        menuItems.Add(new MenuItem
        {
            Header = StaticPagesLanguageManager.Instance.imageviewer_copyImage.CurrentValue(),
            Icon = CreateMenuItemIcon("\ue635"),
            Command = new RelayCommand(async () => await CopyImageAsync())
        });

        menuItems.Add(new MenuItem
        {
            Header = StaticPagesLanguageManager.Instance.imageviewer_saveAs.CurrentValue(),
            Icon = CreateMenuItemIcon("\ue632"),
            Command = new RelayCommand(async () => await SaveAsAsync())
        });
    }

    private static TextBlock CreateMenuItemIcon(string glyph)
    {
        return new TextBlock
        {
            FontFamily = IconResources.IconFont,
            FontSize = 18,
            FontWeight = FontWeight.Thin,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Text = glyph
        };
    }

    private async Task OpenWithDefaultAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        if (IsRemote(FilePath))
        {
            if (Uri.TryCreate(FilePath, UriKind.Absolute, out var uri))
                await topLevel.Launcher.LaunchUriAsync(uri);
            return;
        }

        var storage = topLevel.StorageProvider;
        if (storage == null || !File.Exists(FilePath))
            return;

        var file = await storage.TryGetFileFromPathAsync(new Uri(FilePath));
        if (file != null)
            await topLevel.Launcher.LaunchFileAsync(file);
    }

    private async Task CopyImageAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null || Image == null)
            return;

        await clipboard.SetBitmapAsync(Image);
    }

    private async Task SaveAsAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || Image == null)
            return;

        var destination = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = StaticPagesLanguageManager.Instance.imageviewer_saveAs.CurrentValue(),
            SuggestedFileName = Path.GetFileNameWithoutExtension(FileName) + ".png",
            FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }]
        });
        if (destination == null)
            return;

        await using var stream = await destination.OpenWriteAsync();
        Image.Save(stream, PngBitmapEncoderOptions.Default);
    }

    public static void Open(string filePath, TopLevel sender, IReadOnlyList<string>? filePaths = null)
    {
        if (!File.Exists(filePath) || sender is not TioTabWindowBase window)
            return;

        var items = filePaths is { Count: > 0 }
            ? filePaths.Select(path => new ImageViewerItem(path, Path.GetFileName(path))).ToArray()
            : [new ImageViewerItem(filePath, Path.GetFileName(filePath))];

        var currentIndex = -1;
        for (var i = 0; i < items.Length; i++)
        {
            if (string.Equals(items[i].Source, filePath, StringComparison.OrdinalIgnoreCase))
            {
                currentIndex = i;
                break;
            }
        }

        Open(items, currentIndex, sender);
    }

    public static void Open(IReadOnlyList<ImageViewerItem> items, int currentIndex, TopLevel sender)
    {
        if (items.Count == 0 || currentIndex < 0 || currentIndex >= items.Count ||
            sender is not TioTabWindowBase window)
            return;

        var item = items[currentIndex];
        var tab = new TabEntry(window, new ImageViewer(item.Source, items));
        window.CreateTab(tab);
        window.SelectTab(tab);
    }

    private static bool IsRemote(string source)
    {
        return source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private void NavigatePrevious_OnClick(object? sender, RoutedEventArgs e)
    {
        NavigateTo(_currentIndex - 1);
    }

    private void NavigateNext_OnClick(object? sender, RoutedEventArgs e)
    {
        NavigateTo(_currentIndex + 1);
    }

    private void NavigateTo(int index)
    {
        if (index < 0 || index >= _items.Count || index == _currentIndex)
            return;

        _currentIndex = index;
        FilePath = _items[index].Source;
        FileName = _items[index].DisplayName;

        PageInfo.Title = FileName;
        if (HostTab != null)
        {
            HostTab.Title = FileName;
            HostTab.Header = FileName;
        }

        Image?.Dispose();
        _image = null;
        _ = LoadImageAsync();

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanNavigatePrevious)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanNavigateNext)));
    }

    private void ZoomIn_OnClick(object? sender, RoutedEventArgs e)
    {
        ImageScrollView.ZoomTo(Math.Clamp(ImageScrollView.ZoomFactor + 0.1, 0.1, 100));
    }

    private void ZoomOut_OnClick(object? sender, RoutedEventArgs e)
    {
        ImageScrollView.ZoomTo(Math.Clamp(ImageScrollView.ZoomFactor - 0.1, 0.1, 100));
    }
}

public sealed record ImageViewerItem(string Source, string DisplayName);
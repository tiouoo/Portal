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
namespace Portal.Views.Pages.StaticPages;

public partial class ImageViewer : UserControl, ITioTabPage, IContextMenuTabPage, INotifyPropertyChanged, IDisposable
{
    private readonly IReadOnlyList<string> _filePaths;
    private bool _isDisposed;
    private Bitmap? _image;
    private int _currentIndex;
    private int _loadVersion;

    public ImageViewer() : this(string.Empty, null)
    {
    }

    public ImageViewer(string filePath) : this(filePath, null)
    {
    }

    public ImageViewer(string filePath, IReadOnlyList<string>? filePaths)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);

        _filePaths = filePaths is { Count: > 0 } ? filePaths : [filePath];
        _currentIndex = -1;
        for (var i = 0; i < _filePaths.Count; i++)
        {
            if (string.Equals(_filePaths[i], filePath, StringComparison.OrdinalIgnoreCase))
            {
                _currentIndex = i;
                break;
            }
        }

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

    public bool CanNavigateNext => _currentIndex >= 0 && _currentIndex < _filePaths.Count - 1;

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
        if (_isDisposed || !File.Exists(FilePath))
            return;

        var version = ++_loadVersion;
        Bitmap? bitmap;
        try
        {
            bitmap = await Task.Run(() => new Bitmap(FilePath));
        }
        catch (Exception)
        {
            return;
        }

        if (_isDisposed || version != _loadVersion)
        {
            bitmap.Dispose();
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
            Header = StaticPagesLanguageManager.Instance.imageviewer_openWithDefault.CurrentValue(),
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
        var storage = topLevel?.StorageProvider;
        if (topLevel == null || storage == null || !File.Exists(FilePath))
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

        var tab = new TabEntry(window, new ImageViewer(filePath, filePaths));
        window.CreateTab(tab);
        window.SelectTab(tab);
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
        if (index < 0 || index >= _filePaths.Count || index == _currentIndex)
            return;

        _currentIndex = index;
        FilePath = _filePaths[index];
        FileName = Path.GetFileName(FilePath);

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
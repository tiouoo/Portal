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
    private bool _isDisposed;
    private Bitmap? _image;

    public ImageViewer() : this(string.Empty)
    {
    }

    public ImageViewer(string filePath)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);

        PageInfo = new PageInfo
        {
            Title = FileName,
            IconGlyph = "\ue632", IconFont = IconResources.FontFamilyName
        };

        InitializeComponent();
        DataContext = this;

        _ = LoadImageAsync();
    }

    public string FilePath { get; }
    public string FileName { get; }

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

        Bitmap? bitmap;
        try
        {
            bitmap = await Task.Run(() => new Bitmap(FilePath));
        }
        catch (Exception)
        {
            return;
        }

        if (_isDisposed)
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

    public static void Open(string filePath, TopLevel sender)
    {
        if (!File.Exists(filePath) || sender is not TioTabWindowBase window)
            return;

        var tab = new TabEntry(window, new ImageViewer(filePath));
        window.CreateTab(tab);
        window.SelectTab(tab);
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
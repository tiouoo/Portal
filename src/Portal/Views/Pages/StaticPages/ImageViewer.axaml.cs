using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

using Portal.Module;
namespace Portal.Views.Pages.StaticPages;

public partial class ImageViewer : UserControl, ITioTabPage, IDisposable
{
    private bool _isDisposed;

    public ImageViewer() : this(string.Empty)
    {
    }

    public ImageViewer(string filePath)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);

        if (File.Exists(filePath))
            try
            {
                Image = new Bitmap(filePath);
            }
            catch (ArgumentException)
            {
            }

        PageInfo = new PageInfo
        {
            Title = FileName,
            IconGlyph = "\ue632", IconFont = IconResources.FontFamilyName
        };

        InitializeComponent();
        DataContext = this;
    }

    public string FilePath { get; }
    public string FileName { get; }
    public Bitmap? Image { get; }

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
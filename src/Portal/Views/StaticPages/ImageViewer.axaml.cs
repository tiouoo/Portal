using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Portal.Views.Pages;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Views.StaticPages;

public partial class ImageViewer : UserControl, ITioTabPage, IDisposable
{
    public string FilePath { get; }
    public string FileName { get; }
    public Bitmap? Image { get; }

    public ImageViewer() : this(string.Empty)
    {
    }

    public ImageViewer(string filePath)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);

        if (File.Exists(filePath))
        {
            try
            {
                Image = new Bitmap(filePath);
            }
            catch (ArgumentException)
            {
                // Unsupported or corrupt images remain openable from the gallery's context menu.
            }
        }

        PageInfo = new PageInfo
        {
            Title = FileName,
            Icon = StreamGeometry.Parse(
                "F1 M640,640z M0,0z M128,128C128,92.7,156.7,64,192,64L341.5,64C358.5,64,374.8,70.7,386.8,82.7L493.3,189.3C505.3,201.3,512,217.6,512,234.6L512,512C512,547.3,483.3,576,448,576L192,576C156.7,576,128,547.3,128,512L128,128z M336,122.5L336,216C336,229.3,346.7,240,360,240L453.5,240 336,122.5z M256,320C256,302.3 241.7,288 224,288 206.3,288 192,302.3 192,320 192,337.7 206.3,352 224,352 241.7,352 256,337.7 256,320z M220.6,512L419.4,512C435.2,512 448,499.2 448,483.4 448,476.1 445.2,469 440.1,463.7L343.3,361.9C337.3,355.6,328.9,352,320.1,352L319.8,352C311,352,302.7,355.6,296.6,361.9L199.9,463.7C194.8,469 192,476.1 192,483.4 192,499.2 204.8,512 220.6,512z")
        };

        InitializeComponent();
        DataContext = this;
    }

    public PageInfo PageInfo { get; init; }
    public TabEntry HostTab { get; set; }

    public static void Open(string filePath, TopLevel sender)
    {
        if (!File.Exists(filePath) || sender is not TioTabWindowBase window)
            return;

        var tab = new TabEntry(window, new ImageViewer(filePath));
        window.CreateTab(tab);
        window.SelectTab(tab);
    }

    private void ZoomIn_OnClick(object? sender, RoutedEventArgs e) =>
        ImageScrollView.ZoomTo(Math.Clamp(ImageScrollView.ZoomFactor + 0.1, 0.1, 100));

    private void ZoomOut_OnClick(object? sender, RoutedEventArgs e) =>
        ImageScrollView.ZoomTo(Math.Clamp(ImageScrollView.ZoomFactor - 0.1, 0.1, 100));

    public void Dispose() => Image?.Dispose();
}

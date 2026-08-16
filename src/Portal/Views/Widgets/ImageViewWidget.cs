using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Portal.Classes.Entries;
using Portal.Module.Widgets;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Views.Widgets;

public sealed class ImageViewWidget : IWidgetContent
{
    private readonly Image _image;
    private readonly Border _root;
    private ImageWidgetData? _data;

        private bool _stretchFill = true;

    public ImageViewWidget(WidgetCellSize size)
    {
        Size = size;

        
        
        _root = new Border
        {
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Background = new SolidColorBrush(Colors.Transparent)
        };
        _image = new Image
        {
            Stretch = Stretch.UniformToFill
        };
        _root.Child = _image;
        Content = _root;
    }

    public override void Initialize(WidgetLayoutData layout)
    {
        _data = layout.Data as ImageWidgetData;
        
        if (_data == null)
        {
            _data = new ImageWidgetData();
            layout.Data = _data;
        }
        _stretchFill = _data.StretchFill ?? true;
        ApplyStretch();
        ReloadImage();
    }

        public void ToggleStretchMode()
    {
        _stretchFill = !_stretchFill;
        if (_data != null)
            _data.StretchFill = _stretchFill;
        ApplyStretch();
    }

        public void UpdateImage(string? path)
    {
        if (_data != null)
            _data.ImagePath = path;
        ReloadImage();
    }

    private void ApplyStretch()
    {
        _image.Stretch = _stretchFill ? Stretch.UniformToFill : Stretch.Uniform;
    }

    private void ReloadImage()
    {
        var path = _data?.ImagePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            _image.Source = null;
            return;
        }

        try
        {
            
            using var fs = File.OpenRead(path);
            _image.Source = new Bitmap(fs);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Widget] Failed to load image {path}: {exception}");
            _image.Source = null;
        }
    }
}

using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Portal.Classes.Entries;
using Portal.Module.Widgets;

namespace Portal.Views.Widgets;

/// <summary>
/// 图片小组件。显示一张本地图片，完全占满组件区域（无内边距）。
/// 通过 <see cref="WidgetLayoutData.Data"/> 中的 <see cref="ImageWidgetData"/> 持久化图片路径与填充方式。
/// </summary>
public sealed class ImageViewWidget : IWidgetContent
{
    private readonly Image _image;
    private readonly Border _root;
    private ImageWidgetData? _data;

    /// <summary>填充方式：true=裁剪填充（UniformToFill），false=完整显示（Uniform）。</summary>
    private bool _stretchFill = true;

    public ImageViewWidget(WidgetCellSize size)
    {
        Size = size;

        // WidgetHost.ContentHost 有 Margin=12，这里用 Margin=-12 抵消，
        // 让图片完全占满整个 Card 区域；CornerRadius + ClipToBounds 让图片被圆角裁剪。
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
        // 兼容旧配置：Data 缺失时补建，避免后续更换图片时无处持久化。
        if (_data == null)
        {
            _data = new ImageWidgetData();
            layout.Data = _data;
        }
        _stretchFill = _data.StretchFill ?? true;
        ApplyStretch();
        ReloadImage();
    }

    /// <summary>切换填充方式并持久化。</summary>
    public void ToggleStretchMode()
    {
        _stretchFill = !_stretchFill;
        if (_data != null)
            _data.StretchFill = _stretchFill;
        ApplyStretch();
    }

    /// <summary>更新图片路径并刷新显示。路径为空时清空。</summary>
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
            // 用 FileStream 读取，避免持有文件锁，可随时更换或删除源文件。
            using var fs = File.OpenRead(path);
            _image.Source = new Bitmap(fs);
        }
        catch
        {
            _image.Source = null;
        }
    }
}

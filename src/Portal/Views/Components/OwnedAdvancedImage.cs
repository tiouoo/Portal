using System;
using AsyncImageLoader;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Portal.Views.Components;

/// <summary>
/// <see cref="AdvancedImage"/> 从不释放 <see cref="IAsyncImageLoader"/> 返回的位图，
/// 列表滚动、翻页、切换数据源时旧位图只能等终结器回收，非托管内存会持续堆积。
/// 本控件在图片被替换时立即释放上一张位图。
/// </summary>
/// <remarks>
/// 只能配合“每次调用返回独立位图”的加载器使用（见 <see cref="Portal.Module.Imaging.DiskCachedImageLoader"/>）。
/// 切勿与 AsyncImageLoader 自带的内存缓存加载器搭配，那些位图是共享的，释放后会被其他控件继续使用。
/// </remarks>
public class OwnedAdvancedImage : AdvancedImage
{
    public OwnedAdvancedImage(Uri? baseUri) : base(baseUri)
    {
    }

    public OwnedAdvancedImage(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    protected override Type StyleKeyOverride => typeof(AdvancedImage);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != CurrentImageProperty)
            return;

        var previous = change.GetOldValue<IImage?>();

        // FallbackImage 由调用方持有、可能被多个控件共享，不能释放。
        if (previous is not Bitmap bitmap || ReferenceEquals(previous, FallbackImage))
            return;

        // 延后到下一次渲染之后释放，避免合成器仍在使用该位图。
        Dispatcher.UIThread.Post(bitmap.Dispose, DispatcherPriority.Background);
    }
}

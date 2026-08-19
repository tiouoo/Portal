using AsyncImageLoader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Portal.Module.Imaging;

namespace Portal.Views.Components;

public class OwnedAdvancedImage : AdvancedImage
{
    private bool _isAttached;

    public OwnedAdvancedImage(Uri? baseUri) : base(baseUri)
    {
        DataContextChanged += (_, _) => Dispatcher.UIThread.Post(ReloadIfNeeded, DispatcherPriority.Background);
    }

    public OwnedAdvancedImage(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        DataContextChanged += (_, _) => Dispatcher.UIThread.Post(ReloadIfNeeded, DispatcherPriority.Background);
    }

    protected override Type StyleKeyOverride => typeof(AdvancedImage);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        Dispatcher.UIThread.Post(ReloadIfNeeded, DispatcherPriority.Background);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _isAttached = false;
    }

    /// <summary>
    /// ItemsRepeater 回收容器时，若容器被重绑到相同 Source 的项，Source 不触发变化事件，
    /// 且中断的异步加载会留下 CurrentImage=null，导致图片永久空白（仅出现在可见区）。
    /// 此处强制重新触发加载，并在完成后清除本地值以恢复绑定优先级。
    /// </summary>
    private void ReloadIfNeeded()
    {
        if (!_isAttached || IsLoading || CurrentImage is not null || string.IsNullOrWhiteSpace(Source))
            return;
        var source = Source;
        Source = null;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isAttached) return;
            if (!string.IsNullOrWhiteSpace(Source) && !string.Equals(Source, source, StringComparison.Ordinal))
                return;
            Source = source;
            Dispatcher.UIThread.Post(() =>
            {
                if (_isAttached)
                    ClearValue(SourceProperty);
            }, DispatcherPriority.Background);
        }, DispatcherPriority.Background);
    }
}

public sealed class NewsImage : OwnedAdvancedImage
{
    private static readonly IAsyncImageLoader LoaderInstance = new NewsImageLoader();

    public NewsImage(Uri? baseUri) : base(baseUri)
    {
        Loader = LoaderInstance;
    }

    public NewsImage(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        Loader = LoaderInstance;
    }
}

public sealed class XboxAvatarImage : OwnedAdvancedImage
{
    private static readonly IAsyncImageLoader LoaderInstance = new XboxAvatarImageLoader();

    public XboxAvatarImage(Uri? baseUri) : base(baseUri)
    {
        Loader = LoaderInstance;
    }

    public XboxAvatarImage(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        Loader = LoaderInstance;
    }
}
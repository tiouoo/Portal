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
    private int _reloadAttempts;
    private string? _reloadSource;

    public OwnedAdvancedImage(Uri? baseUri) : base(baseUri)
    {
        DataContextChanged += (_, _) => Dispatcher.UIThread.Post(EnsureLoaded, DispatcherPriority.Background);
    }

    public OwnedAdvancedImage(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        DataContextChanged += (_, _) => Dispatcher.UIThread.Post(EnsureLoaded, DispatcherPriority.Background);
    }

    protected override Type StyleKeyOverride => typeof(AdvancedImage);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        Dispatcher.UIThread.Post(EnsureLoaded, DispatcherPriority.Background);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _isAttached = false;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            _reloadSource = null;
            _reloadAttempts = 0;
            Dispatcher.UIThread.Post(EnsureLoaded, DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// ItemsRepeater 回收容器时，若容器被重绑到相同 Source 的项，Source 不触发变化事件，
    /// 且被取消的异步加载会让 AdvancedImage 卡在 IsLoading=true、CurrentImage=null 的状态。
    /// 这里不信任 IsLoading，在源已设置但没有图片时强制重新触发加载（同一源只强制一次，
    /// 避免与真实慢加载互相打断），并在完成后清除本地值以恢复绑定优先级。
    /// </summary>
    private void EnsureLoaded()
    {
        if (!_isAttached || string.IsNullOrWhiteSpace(Source) || CurrentImage is not null)
            return;
        var source = Source;
        if (string.Equals(_reloadSource, source, StringComparison.Ordinal) && _reloadAttempts >= 1)
            return;

        _reloadSource = source;
        _reloadAttempts++;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isAttached || !string.Equals(Source, source, StringComparison.Ordinal))
                return;
            if (CurrentImage is not null)
                return;
            Source = null;
            Dispatcher.UIThread.Post(() =>
            {
                if (_isAttached && string.Equals(Source, null, StringComparison.Ordinal))
                    Source = source;
                Dispatcher.UIThread.Post(() =>
                {
                    if (_isAttached)
                        ClearValue(SourceProperty);
                }, DispatcherPriority.Background);
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
using System;
using AsyncImageLoader;
using Avalonia;
using Portal.Module.Imaging;

namespace Portal.Views.Components;

/// <summary>
/// 新闻等磁盘缓存图片使用的 <see cref="AdvancedImage"/>，不主动管理加载结果的生命周期。
/// </summary>
public class OwnedAdvancedImage : AdvancedImage
{
    public OwnedAdvancedImage(Uri? baseUri) : base(baseUri)
    {
    }

    public OwnedAdvancedImage(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    protected override Type StyleKeyOverride => typeof(AdvancedImage);
}

/// <summary>
/// 新闻封面专用图片控件。在 Source 绑定前固定加载器，确保始终先读取磁盘缓存。
/// </summary>
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

using System;
using AsyncImageLoader;
using Avalonia;
using Portal.Module.Imaging;

namespace Portal.Views.Components;

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

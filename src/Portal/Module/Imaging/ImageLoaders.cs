using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AsyncImageLoader;
using Avalonia.Media.Imaging;

namespace Portal.Module.Imaging;

/// <summary>
/// 模组 / 资源列表与详情页的图标加载器（磁盘缓存 + 小尺寸解码）。
/// </summary>
public sealed class ModImageLoader() : DiskCachedImageLoader("#mod-images", 56);

/// <summary>
/// 详情页截图加载器。
/// </summary>
public sealed class ModScreenshotLoader() : DiskCachedImageLoader("#mod-screenshots", 260);

/// <summary>
/// 新闻封面加载器。以前这些图片走 AsyncImageLoader 的全局内存缓存，
/// 关闭标签页后依然常驻内存；改为磁盘缓存后由视图负责释放位图。
/// </summary>
public sealed class NewsImageLoader() : DiskCachedImageLoader("#news-images", 520);

/// <summary>
/// 程序集嵌入资源图片加载器（resm: URI）：按需解码到目标宽度，不做内存缓存。
/// 以前这些图片走 AdvancedImage 默认的全局内存缓存，打开一次后位图永久驻留。
/// </summary>
public sealed class ResourceImageLoader(int decodeWidth) : IAsyncImageLoader
{
    public Task<Bitmap?> ProvideImageAsync(string url) => Task.Run<Bitmap?>(() =>
    {
        // 形如 resm:Portal.Core.Assets.McIcons.xxx.png?assembly=Portal.Core
        if (!url.StartsWith("resm:", StringComparison.OrdinalIgnoreCase))
            return null;

        var separator = url.IndexOf("?assembly=", StringComparison.OrdinalIgnoreCase);
        var resourceName = separator < 0 ? url["resm:".Length..] : url["resm:".Length..separator];
        var assemblyName = separator < 0 ? null : url[(separator + "?assembly=".Length)..];
        var assembly = assemblyName == null
            ? Assembly.GetExecutingAssembly()
            : AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => candidate.GetName().Name == assemblyName);
        if (assembly == null)
            return null;

        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            return stream == null ? null : Bitmap.DecodeToWidth(stream, decodeWidth);
        }
        catch (IOException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    });

    public void Dispose()
    {
    }
}

/// <summary>
/// 本地图片（截图、存档图标等）加载器：仅按需解码到目标宽度，不做内存缓存。
/// </summary>
public sealed class LocalImageLoader(int decodeWidth) : IAsyncImageLoader
{
    public Task<Bitmap?> ProvideImageAsync(string url) => Task.Run<Bitmap?>(() =>
    {
        try
        {
            using var stream = File.OpenRead(url);
            return Bitmap.DecodeToWidth(stream, decodeWidth);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    });

    public void Dispose()
    {
    }
}

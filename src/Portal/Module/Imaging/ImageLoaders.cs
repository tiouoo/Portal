using System.Reflection;
using AsyncImageLoader;
using Avalonia.Media.Imaging;

namespace Portal.Module.Imaging;

public sealed class ModImageLoader() : DiskCachedImageLoader("#mod-images", 56);

public sealed class ModScreenshotLoader() : DiskCachedImageLoader("#mod-screenshots", 260);

public sealed class NewsImageLoader() : DiskCachedImageLoader("#news-images", 520);

public sealed class XboxAvatarImageLoader() : DiskCachedImageLoader("#xbox-avatars", 128);

public sealed class ResourceImageLoader(int decodeWidth) : IAsyncImageLoader
{
    public Task<Bitmap?> ProvideImageAsync(string url)
    {
        return Task.Run(() =>
        {
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
    }

    public void Dispose()
    {
    }
}

public sealed class LocalImageLoader(int decodeWidth) : IAsyncImageLoader
{
    public Task<Bitmap?> ProvideImageAsync(string url)
    {
        return Task.Run<Bitmap?>(() =>
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
    }

    public void Dispose()
    {
    }
}
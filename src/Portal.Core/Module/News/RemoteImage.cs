using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MinecraftLaunch.Utilities;

namespace Portal.Core.Module.News;

/// <summary>异步从 URL 加载位图的 <see cref="Image"/>，用于渲染正文中的 <c>&lt;img&gt;</c>。</summary>
public sealed class RemoteImage : Image
{
    private long _generation;

    public static readonly StyledProperty<string?> ImageUrlProperty =
        AvaloniaProperty.Register<RemoteImage, string?>(nameof(ImageUrl));

    public string? ImageUrl
    {
        get => GetValue(ImageUrlProperty);
        set => SetValue(ImageUrlProperty, value);
    }

    static RemoteImage()
    {
        ImageUrlProperty.Changed.AddClassHandler<RemoteImage>((image, _) => image.Reload());
    }

    public RemoteImage()
    {
        Stretch = Stretch.Uniform;
        MaxHeight = 480;
        MaxWidth = 720;
        ClipToBounds = true;
    }

    private void Reload()
    {
        var url = ImageUrl;
        ReplaceSource(null);
        if (string.IsNullOrWhiteSpace(url)) return;

        var generation = Interlocked.Increment(ref _generation);
        _ = LoadAsync(url, generation);
    }

    private void ReplaceSource(IImage? bitmap)
    {
        var previous = Source;
        Source = bitmap;
        if (previous is IDisposable disposable && !ReferenceEquals(previous, bitmap))
            disposable.Dispose();
    }

    private async Task LoadAsync(string url, long generation)
    {
        try
        {
            using var response = await HttpUtil.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (generation != Volatile.Read(ref _generation)) return;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (generation != Volatile.Read(ref _generation)) return;

            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = await Task.Run(() => new Bitmap(stream));
            if (generation != Volatile.Read(ref _generation))
            {
                bitmap.Dispose();
                return;
            }

            ReplaceSource(bitmap);
        }
        catch
        {
        }
    }
}

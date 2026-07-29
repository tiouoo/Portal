using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AsyncImageLoader;
using Avalonia.Media.Imaging;
using Portal.Const;
using MinecraftLaunch.Utilities;

namespace Portal.Module.Imaging;

/// <summary>
/// 把远程图片缓存到磁盘、并按需解码成指定宽度的位图。
/// </summary>
/// <remarks>
/// 与 AsyncImageLoader 自带的 <c>RamCachedWebImageLoader</c> 不同，这里不做任何内存缓存：
/// 每次调用都返回一张独立的位图，其生命周期完全由 <see cref="Portal.Views.Components.OwnedAdvancedImage"/> 负责。
/// 页面关闭或列表项被回收时位图会立即释放，不会像全局内存缓存那样无限累积。
/// </remarks>
public class DiskCachedImageLoader : IAsyncImageLoader
{
    // 同一个 URL 只下载一次；下载完成后立刻移除信号量，避免字典无限增长。
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DownloadLocks = new();

    private readonly int _decodeWidth;
    private readonly string _cacheCategory;

    protected DiskCachedImageLoader(string cacheCategory, int decodeWidth)
    {
        _cacheCategory = cacheCategory;
        _decodeWidth = decodeWidth;
    }

    public async Task<Bitmap?> ProvideImageAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            var cachePath = GetCachePath(url);
            if (File.Exists(cachePath))
                return Decode(cachePath);

            var downloadLock = DownloadLocks.GetOrAdd(cachePath, _ => new SemaphoreSlim(1, 1));
            await downloadLock.WaitAsync();
            try
            {
                if (File.Exists(cachePath))
                    return Decode(cachePath);

                using var response = await HttpUtil.Client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync();
                using var bitmap = Bitmap.DecodeToWidth(stream, _decodeWidth);
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                // 临时文件名带随机后缀，避免同一 URL 的并发写入互相冲突。
                var temporaryPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
                using (var output = File.Create(temporaryPath))
                    bitmap.Save(output, PngBitmapEncoderOptions.Default);
                File.Move(temporaryPath, cachePath, true);
                return Decode(cachePath);
            }
            finally
            {
                downloadLock.Release();
                // 只摘除条目、不释放信号量：可能还有等待者持有它，交给 GC 回收即可。
                DownloadLocks.TryRemove(cachePath, out _);
            }
        }
        catch (HttpRequestException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (InvalidDataException) { return null; }
        catch (ArgumentException) { return null; }
    }

    private Bitmap Decode(string cachePath)
    {
        using var stream = File.OpenRead(cachePath);
        return Bitmap.DecodeToWidth(stream, _decodeWidth);
    }

    private string GetCachePath(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        return Path.Combine(ConfigPath.CacheFolderPath, _cacheCategory, hash[..2], hash + ".png");
    }

    public void Dispose()
    {
    }
}

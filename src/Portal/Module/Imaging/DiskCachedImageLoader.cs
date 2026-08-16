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
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Module.Imaging;

public class DiskCachedImageLoader : IAsyncImageLoader
{
    
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
            {
                Logger.Debug($"读取图片磁盘缓存：{cachePath}");
                return Decode(cachePath);
            }

            var downloadLock = DownloadLocks.GetOrAdd(cachePath, _ => new SemaphoreSlim(1, 1));
            await downloadLock.WaitAsync();
            try
            {
                if (File.Exists(cachePath))
                    return Decode(cachePath);

                using var response = await HttpUtil.Client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                Logger.Debug($"下载远程图片并写入缓存：{url} -> {cachePath}");
                await using var stream = await response.Content.ReadAsStreamAsync();
                using var bitmap = Bitmap.DecodeToWidth(stream, _decodeWidth);
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                
                var temporaryPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
                using (var output = File.Create(temporaryPath))
                    bitmap.Save(output, PngBitmapEncoderOptions.Default);
                File.Move(temporaryPath, cachePath, true);
                return Decode(cachePath);
            }
            finally
            {
                downloadLock.Release();
                
                DownloadLocks.TryRemove(cachePath, out _);
            }
        }
        catch (HttpRequestException exception) { Logger.Error($"下载远程图片失败：{url}", exception); return null; }
        catch (IOException exception) { Logger.Error($"读写图片缓存失败：{url}", exception); return null; }
        catch (UnauthorizedAccessException exception) { Logger.Error($"访问图片缓存被拒绝：{url}", exception); return null; }
        catch (InvalidDataException exception) { Logger.Error($"解析远程图片失败：{url}", exception); return null; }
        catch (ArgumentException exception) { Logger.Error($"处理远程图片地址失败：{url}", exception); return null; }
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

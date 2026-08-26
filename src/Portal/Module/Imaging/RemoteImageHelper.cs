using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using MinecraftLaunch.Utilities;
using Portal.Core.Const;

namespace Portal.Module.Imaging;

/// <summary>
/// 将远程图片下载到磁盘缓存并返回本地路径，供需要完整原始分辨率的场景（如图片查看器）使用。
/// </summary>
public static class RemoteImageHelper
{
    private const string CacheCategory = "#imageviewer";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DownloadLocks = new();

    public static string? GetCachedPath(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var cachePath = GetCachePath(url);
        return File.Exists(cachePath) ? cachePath : null;
    }

    public static async Task<string?> EnsureLocalAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var cachePath = GetCachePath(url);
        if (File.Exists(cachePath))
            return cachePath;

        var downloadLock = DownloadLocks.GetOrAdd(cachePath, _ => new SemaphoreSlim(1, 1));
        await downloadLock.WaitAsync();
        try
        {
            if (File.Exists(cachePath))
                return cachePath;

            using var response = await HttpUtil.Client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var bitmap = new Bitmap(stream);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            var temporaryPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
            using (var output = File.Create(temporaryPath))
            {
                bitmap.Save(output, PngBitmapEncoderOptions.Default);
            }

            File.Move(temporaryPath, cachePath, true);
            return cachePath;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            downloadLock.Release();
            DownloadLocks.TryRemove(cachePath, out _);
        }
    }

    private static string GetCachePath(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        return Path.Combine(ConfigPath.CacheFolderPath, CacheCategory, hash[..2], hash + ".png");
    }
}
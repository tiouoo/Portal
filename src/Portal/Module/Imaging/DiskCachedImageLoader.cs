using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AsyncImageLoader;
using Avalonia.Media.Imaging;
using MinecraftLaunch.Utilities;
using Portal.Core.Const;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Module.Imaging;

public class DiskCachedImageLoader : IAsyncImageLoader
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DownloadLocks = new();
    private readonly string _cacheCategory;

    private readonly int _decodeWidth;

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
                Logger.Debug(string.Format(
                    LogLanguageManager.Instance.imaging_readImageDiskCache.CurrentValue(), cachePath));
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
                Logger.Debug(string.Format(
                    LogLanguageManager.Instance.imaging_downloadImageWriteCache.CurrentValue(), url, cachePath));
                await using var stream = await response.Content.ReadAsStreamAsync();
                using var bitmap = Bitmap.DecodeToWidth(stream, _decodeWidth);
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

                var temporaryPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
                using (var output = File.Create(temporaryPath))
                {
                    bitmap.Save(output, PngBitmapEncoderOptions.Default);
                }

                File.Move(temporaryPath, cachePath, true);
                return Decode(cachePath);
            }
            finally
            {
                downloadLock.Release();

                DownloadLocks.TryRemove(cachePath, out _);
            }
        }
        catch (HttpRequestException exception)
        {
            Logger.Error(string.Format(
                LogLanguageManager.Instance.imaging_downloadRemoteImageFailed.CurrentValue(), url), exception);
            return null;
        }
        catch (IOException exception)
        {
            Logger.Error(string.Format(
                LogLanguageManager.Instance.imaging_readWriteImageCacheFailed.CurrentValue(), url), exception);
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            Logger.Error(string.Format(
                LogLanguageManager.Instance.imaging_accessImageCacheDenied.CurrentValue(), url), exception);
            return null;
        }
        catch (InvalidDataException exception)
        {
            Logger.Error(string.Format(
                LogLanguageManager.Instance.imaging_parseRemoteImageFailed.CurrentValue(), url), exception);
            return null;
        }
        catch (ArgumentException exception)
        {
            Logger.Error(string.Format(
                LogLanguageManager.Instance.imaging_handleRemoteImageUrlFailed.CurrentValue(), url), exception);
            return null;
        }
    }

    public void Dispose()
    {
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
}
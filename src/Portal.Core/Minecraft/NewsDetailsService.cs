using Flurl.Http;
using Newtonsoft.Json;
using Portal.Core.Helpers;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft;

/// <summary>
/// 新闻详情正文获取与缓存服务。
/// 仅在用户实际打开某条新闻详情时调用 <see cref="GetAsync"/>，命中缓存则直接返回，否则从 Mojang 拉取并缓存。
/// </summary>
public static class NewsDetailsService
{
    private const string BaseContentUrl = "https://launchercontent.mojang.com/v2/";
    private const string BaseImageUrl = "https://launchercontent.mojang.com";

    // 进程内 LRU 缓存，避免重复打开同一条新闻时反复查 SQLite。
    private static readonly LruCache<string, NewsDetail?> MemoryCache = new(32, StringComparer.Ordinal);

    /// <summary>
    /// 获取新闻详情正文。优先返回缓存（内存 → SQLite），未命中则向 Mojang 拉取并缓存。
    /// </summary>
    public static async Task<NewsDetail?> GetAsync(NewsEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Id)) return null;

        if (MemoryCache.TryGetValue(entry.Id, out var cached)) return cached;

        var fromDb = CacheDatabase.ReadNewsDetail(entry.Id);
        if (fromDb != null)
        {
            MemoryCache.Set(entry.Id, fromDb);
            return fromDb;
        }

        var fetched = await FetchAsync(entry);
        if (fetched != null)
        {
            CacheDatabase.WriteNewsDetail(fetched);
            MemoryCache.Set(entry.Id, fetched);
        }
        return fetched;
    }

    /// <summary>强制重新拉取并覆盖缓存（用于刷新按钮等场景）。</summary>
    public static async Task<NewsDetail?> RefreshAsync(NewsEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Id)) return null;
        var fetched = await FetchAsync(entry);
        if (fetched != null)
        {
            CacheDatabase.WriteNewsDetail(fetched);
            MemoryCache.Set(entry.Id, fetched);
        }
        return fetched;
    }

    private static async Task<NewsDetail?> FetchAsync(NewsEntry entry)
    {
        if (string.IsNullOrEmpty(entry.ContentPath)) return null;
        try
        {
            var url = BaseContentUrl + entry.ContentPath;
            var json = await url.GetStringAsync();
            var content = JsonConvert.DeserializeObject<NewsContentResponse>(json);
            if (content == null) return null;

            var imageUrl = entry.ImageUrl;
            if (string.IsNullOrEmpty(imageUrl) && !string.IsNullOrEmpty(content.Image?.Url))
            {
                imageUrl = content.Image.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? content.Image.Url
                    : BaseImageUrl + content.Image.Url;
            }

            return new NewsDetail
            {
                Id = entry.Id,
                Title = string.IsNullOrEmpty(content.Title) ? entry.Title : content.Title,
                Version = string.IsNullOrEmpty(content.Version) ? entry.Version : content.Version,
                Type = string.IsNullOrEmpty(content.Type) ? entry.Type : content.Type,
                ImageUrl = imageUrl,
                Date = entry.Date,
                Body = content.Body ?? string.Empty,
                FetchedAt = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"获取新闻详情失败 ({entry.ContentPath}): {ex.Message}");
            return null;
        }
    }
}

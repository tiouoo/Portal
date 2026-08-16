using Newtonsoft.Json;
using Portal.Core.App.Helpers;
using Portal.Core.Helpers;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft;

public static class NewsDetailsService
{
    private const string BaseContentUrl = "https://mcnews.tiouo.cc/v2/";
    
    private const string BaseImageUrl = "https://launchercontent.mojang.com";

    
    private static readonly LruCache<string, NewsDetail?> MemoryCache = new(32, StringComparer.Ordinal);

    
    private static readonly HashSet<string> RefreshingIds = new(StringComparer.Ordinal);
    private static readonly object RefreshLock = new();

        public static event Action<NewsDetail>? NewsDetailUpdated;

        public static async Task<NewsDetail?> GetAsync(NewsEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Id)) return null;

        if (MemoryCache.TryGetValue(entry.Id, out var cached))
        {
            TryBackgroundRefreshIfNeedsTranslation(entry, cached);
            return cached;
        }

        var fromDb = CacheDatabase.ReadNewsDetail(entry.Id);
        if (fromDb != null)
        {
            MemoryCache.Set(entry.Id, fromDb);
            TryBackgroundRefreshIfNeedsTranslation(entry, fromDb);
            return fromDb;
        }

        var fetched = await FetchAsync(entry);
        if (fetched != null)
        {
            CacheDatabase.WriteNewsDetail(fetched);
            MemoryCache.Set(entry.Id, fetched);
            
            TryBackgroundRefreshIfNeedsTranslation(entry, fetched);
        }
        return fetched;
    }

        public static async Task<NewsDetail?> RefreshAsync(NewsEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Id)) return null;
        var fetched = await FetchAsync(entry);
        if (fetched != null)
        {
            CacheDatabase.WriteNewsDetail(fetched);
            MemoryCache.Set(entry.Id, fetched);
            
            TryBackgroundRefreshIfNeedsTranslation(entry, fetched);
        }
        return fetched;
    }

        private static void TryBackgroundRefreshIfNeedsTranslation(NewsEntry entry, NewsDetail? cached)
    {
        if (cached?.NeedsTranslation != true) return;

        lock (RefreshLock)
        {
            if (!RefreshingIds.Add(entry.Id)) return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var fetched = await FetchAsync(entry);
                if (fetched == null) return;

                
                if (fetched.NeedsTranslation == true) return;

                
                CacheDatabase.WriteNewsDetail(fetched);
                MemoryCache.Set(entry.Id, fetched);
                NewsDetailUpdated?.Invoke(fetched);
            }
            catch (Exception ex)
            {
                Logger.Error($"后台刷新翻译新闻失败：{entry.ContentPath}", ex);
            }
            finally
            {
                lock (RefreshLock)
                {
                    RefreshingIds.Remove(entry.Id);
                }
            }
        });
    }

    private static async Task<NewsDetail?> FetchAsync(NewsEntry entry)
    {
        if (string.IsNullOrEmpty(entry.ContentPath)) return null;
        try
        {
            var url = BaseContentUrl + entry.ContentPath;
            var json = await NewsHttp.Client.GetStringAsync(url);
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
                Type = string.IsNullOrEmpty(content.Type) ? entry.Type : entry.Type,
                ImageUrl = imageUrl,
                Date = entry.Date,
                Body = content.Body ?? string.Empty,
                NeedsTranslation = content.NeedsTranslation,
                FetchedAt = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"获取新闻详情失败：{entry.ContentPath}", ex);
            return null;
        }
    }
}

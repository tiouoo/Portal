using Newtonsoft.Json;
using Portal.Core.App.Helpers;
using Portal.Core.Helpers;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft;

/// <summary>
/// 新闻详情正文获取与缓存服务。
/// 仅在用户实际打开某条新闻详情时调用 <see cref="GetAsync"/>，命中缓存则直接返回，否则从镜像源拉取并缓存。
/// 当缓存的正文标记 <c>needsTranslation=true</c> 时，会在后台静默发起一次刷新请求：
/// 若取回的版本已翻译则动态替换缓存与 UI；若仍未翻译则丢弃，保留旧缓存。
/// </summary>
public static class NewsDetailsService
{
    private const string BaseContentUrl = "https://mcnews.tiouo.cc/v2/";
    // 镜像源不托管图片，图片相对路径需拼接官方源。
    private const string BaseImageUrl = "https://launchercontent.mojang.com";

    // 进程内 LRU 缓存，避免重复打开同一条新闻时反复查 SQLite。
    private static readonly LruCache<string, NewsDetail?> MemoryCache = new(32, StringComparer.Ordinal);

    // 正在后台刷新翻译的条目集合，避免对同一条新闻并发重复请求。
    private static readonly HashSet<string> RefreshingIds = new(StringComparer.Ordinal);
    private static readonly object RefreshLock = new();

    /// <summary>
    /// 当后台翻译刷新成功取回已翻译版本时触发。UI 层订阅此事件以动态替换正文。
    /// 可能在非 UI 线程触发，订阅方需自行切换到 UI 线程。
    /// </summary>
    public static event Action<NewsDetail>? NewsDetailUpdated;

    /// <summary>
    /// 获取新闻详情正文。优先返回缓存（内存 → SQLite），未命中则向镜像源拉取并缓存。
    /// 若缓存标记需要翻译，会在返回的同时静默发起后台刷新。
    /// </summary>
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
            // 刚拉取的内容若仍需翻译，也尝试一次后台刷新（翻译可能刚刚就绪）。
            TryBackgroundRefreshIfNeedsTranslation(entry, fetched);
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
            // 手动刷新后若仍需翻译，同样尝试后台刷新。
            TryBackgroundRefreshIfNeedsTranslation(entry, fetched);
        }
        return fetched;
    }

    /// <summary>
    /// 当缓存标记 <c>needsTranslation=true</c> 时，在后台静默发起一次刷新。
    /// 取回的版本若已翻译，则替换缓存（内存 + SQLite）并触发 <see cref="NewsDetailUpdated"/>；
    /// 若仍未翻译则丢弃，保留旧缓存。
    /// </summary>
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

                // 仍未翻译：丢弃，保留旧缓存。
                if (fetched.NeedsTranslation == true) return;

                // 已翻译：替换缓存（内存 + SQLite），并通知 UI 动态替换。
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

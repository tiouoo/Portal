using Flurl.Http;
using Newtonsoft.Json;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft;

public static class NewsService
{
    public static event EventHandler? NewsUpdated;

    public static List<NewsEntry> JavaNews { get; private set; } = [];
    public static List<NewsEntry> BedrockNews { get; private set; } = [];

    private const string JavaApiUrl = "https://mcnews.tiouo.cc/v2/javaPatchNotes";
    private const string BedrockApiUrl = "https://mcnews.tiouo.cc/v2/bedrockPatchNotes";
    // 图片由镜像源返回相对路径（/v2/images/...），但镜像不托管图片，需拼接官方源。
    private const string BaseImageUrl = "https://launchercontent.mojang.com";

    public static void InitializeFromCache()
    {
        JavaNews = LoadCache(NewsEdition.Java);
        BedrockNews = LoadCache(NewsEdition.Bedrock);
    }

    public static void RaiseNewsUpdated()
    {
        if (JavaNews.Count > 0 || BedrockNews.Count > 0)
            NewsUpdated?.Invoke(null, EventArgs.Empty);
    }

    public static async Task FetchAndRefreshAsync()
    {
        try
        {
            var jTask = FetchAsync(JavaApiUrl, NewsEdition.Java);
            var bTask = FetchAsync(BedrockApiUrl, NewsEdition.Bedrock);

            var java = await jTask;
            var bedrock = await bTask;
            bool changed = false;

            if (java?.Count > 0) { JavaNews = java; changed = true; }
            if (bedrock?.Count > 0) { BedrockNews = bedrock; changed = true; }

            if (changed) NewsUpdated?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error($"刷新新闻失败: {ex.Message}");
        }
    }

    private static List<NewsEntry> LoadCache(NewsEdition edition)
    {
        try
        {
            return CacheDatabase.ReadNews(edition);
        }
        catch (Exception ex)
        {
            Logger.Error($"加载新闻缓存失败: {ex.Message}");
            return [];
        }
    }

    private static async Task<List<NewsEntry>?> FetchAsync(string url, NewsEdition edition)
    {
        try
        {
            var json = await url.GetStringAsync();
            var entries = ParseJson(json, edition);
            CacheDatabase.WriteNews(edition, entries);
            return entries;
        }
        catch (Exception ex)
        {
            Logger.Error($"获取新闻失败 ({url}): {ex.Message}");
            return null;
        }
    }

    private static List<NewsEntry> ParseJson(string json, NewsEdition edition)
    {
        var response = JsonConvert.DeserializeObject<PatchNotesResponse>(json);
        return response?.Entries?.Select(e => MapToNewsEntry(e, edition)).ToList() ?? [];
    }

    private static NewsEntry MapToNewsEntry(PatchNoteEntry entry, NewsEdition edition)
    {
        var imageUrl = string.Empty;
        if (!string.IsNullOrEmpty(entry.Image?.Url))
        {
            imageUrl = entry.Image.Url.StartsWith("http") ? entry.Image.Url : BaseImageUrl + entry.Image.Url;
        }

        return new NewsEntry
        {
            Title = entry.Title,
            Version = entry.Version,
            Type = !string.IsNullOrEmpty(entry.Type) ? entry.Type : entry.PatchNoteType,
            ImageUrl = imageUrl,
            ContentPath = entry.ContentPath,
            Id = entry.Id,
            Date = entry.Date.ToLocalTime(),
            ShortText = entry.ShortText,
            NeedsTranslation = entry.NeedsTranslation,
            Edition = edition
        };
    }
}

using Flurl.Http;
using Newtonsoft.Json;
using Portal.Core.Minecraft.Classes;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft;

public static class NewsService
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "xyz.tiouo.Portal");

    public static event EventHandler? NewsUpdated;

    public static List<NewsEntry> JavaNews { get; private set; } = [];
    public static List<NewsEntry> BedrockNews { get; private set; } = [];

    private const string JavaApiUrl = "https://launchercontent.mojang.com/v2/javaPatchNotes.json";
    private const string BedrockApiUrl = "https://launchercontent.mojang.com/v2/bedrockPatchNotes.json";
    private const string BaseImageUrl = "https://launchercontent.mojang.com";

    private static string JavaCachePath => Path.Combine(Root, "java_news_cache.json");
    private static string BedrockCachePath => Path.Combine(Root, "bedrock_news_cache.json");

    public static void InitializeFromCache()
    {
        JavaNews = LoadCache(JavaCachePath, NewsEdition.Java);
        BedrockNews = LoadCache(BedrockCachePath, NewsEdition.Bedrock);
        if (JavaNews.Count > 0 || BedrockNews.Count > 0) NewsUpdated?.Invoke(null, EventArgs.Empty);
    }

    public static async Task FetchAndRefreshAsync()
    {
        try
        {
            var jTask = FetchAsync(JavaApiUrl, JavaCachePath, NewsEdition.Java);
            var bTask = FetchAsync(BedrockApiUrl, BedrockCachePath, NewsEdition.Bedrock);

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

    private static List<NewsEntry> LoadCache(string path, NewsEdition edition)
    {
        try
        {
            if (!File.Exists(path)) return [];
            return ParseJson(File.ReadAllText(path), edition);
        }
        catch (Exception ex)
        {
            Logger.Error($"加载新闻缓存失败: {ex.Message}");
            return [];
        }
    }

    private static async Task<List<NewsEntry>?> FetchAsync(string url, string cachePath, NewsEdition edition)
    {
        try
        {
            var json = await url.GetStringAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            File.WriteAllText(cachePath, json);
            return ParseJson(json, edition);
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
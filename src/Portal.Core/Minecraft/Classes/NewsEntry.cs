using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Localization;

namespace Portal.Core.Minecraft.Classes;

public class NewsEntry : ObservableObject
{
    public string Title { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string ContentPath { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string ShortText { get; set; } = string.Empty;
    public bool? NeedsTranslation { get; set; }
    public NewsEdition Edition { get; set; }
    public string RelativeDate => GetRelativeTime(Date);

    private static string GetRelativeTime(DateTime date)
    {
        var diff = DateTime.Now - date;
        if (diff.TotalMinutes < 1) return CommonLanguageManager.Instance.news_justNow.CurrentValue();
        if (diff.TotalHours < 1) return string.Format(CommonLanguageManager.Instance.news_minutesAgo.CurrentValue(), (int)diff.TotalMinutes);
        if (diff.TotalDays < 1) return string.Format(CommonLanguageManager.Instance.news_hoursAgo.CurrentValue(), (int)diff.TotalHours);
        if (diff.TotalDays < 2) return CommonLanguageManager.Instance.news_yesterday.CurrentValue();
        if (diff.TotalDays < 7) return string.Format(CommonLanguageManager.Instance.news_daysAgo.CurrentValue(), (int)diff.TotalDays);
        if (diff.TotalDays < 30) return string.Format(CommonLanguageManager.Instance.news_weeksAgo.CurrentValue(), (int)(diff.TotalDays / 7));
        if (diff.TotalDays < 365) return string.Format(CommonLanguageManager.Instance.news_monthsAgo.CurrentValue(), (int)(diff.TotalDays / 30));
        return string.Format(CommonLanguageManager.Instance.news_yearsAgo.CurrentValue(), (int)(diff.TotalDays / 365));
    }
}

public enum NewsEdition
{
    Java,
    Bedrock
}

public class PatchNotesResponse
{
    public int Version { get; set; }
    public List<PatchNoteEntry> Entries { get; set; } = [];
}

public class PatchNoteEntry
{
    public string Title { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string PatchNoteType { get; set; } = string.Empty;
    public PatchNoteImage Image { get; set; } = new();
    public string ContentPath { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string ShortText { get; set; } = string.Empty;
    public bool? NeedsTranslation { get; set; }
}

public class PatchNoteImage
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class NewsDetail
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime FetchedAt { get; set; }
    public bool? NeedsTranslation { get; set; }
}

public class NewsContentResponse
{
    public string Title { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public PatchNoteImage? Image { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool? NeedsTranslation { get; set; }
}
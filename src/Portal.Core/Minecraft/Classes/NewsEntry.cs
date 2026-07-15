using CommunityToolkit.Mvvm.ComponentModel;

namespace Portal.Core.Minecraft.Classes;

public partial class NewsEntry : ObservableObject
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
        if (diff.TotalMinutes < 1) return "刚刚";
        if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}分钟前";
        if (diff.TotalDays < 1) return $"{(int)diff.TotalHours}小时前";
        if (diff.TotalDays < 2) return "昨天";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}天前";
        if (diff.TotalDays < 30) return $"{(int)(diff.TotalDays / 7)}周前";
        if (diff.TotalDays < 365) return $"{(int)(diff.TotalDays / 30)}个月前";
        return $"{(int)(diff.TotalDays / 365)}年前";
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

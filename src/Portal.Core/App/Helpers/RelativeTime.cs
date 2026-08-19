namespace Portal.Core.App.Helpers;

public static class RelativeTime
{
    public static string Format(DateTime timestamp)
    {
        var localTime = timestamp.Kind == DateTimeKind.Utc ? timestamp.ToLocalTime() : timestamp;
        var elapsed = DateTime.Now - localTime;
        if (elapsed < TimeSpan.FromMinutes(1)) return "刚刚";
        if (elapsed < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)elapsed.TotalMinutes)} 分钟前";
        if (elapsed < TimeSpan.FromDays(1)) return $"{Math.Max(1, (int)elapsed.TotalHours)} 小时前";
        if (elapsed < TimeSpan.FromDays(2)) return "1 天前";
        if (elapsed < TimeSpan.FromDays(7)) return $"{(int)elapsed.TotalDays} 天前";
        if (elapsed < TimeSpan.FromDays(14)) return "1 周前";
        if (elapsed < TimeSpan.FromDays(30)) return $"{Math.Max(2, (int)(elapsed.TotalDays / 7))} 周前";
        if (elapsed < TimeSpan.FromDays(365)) return $"{Math.Max(1, (int)(elapsed.TotalDays / 30))} 个月前";
        return $"{Math.Max(1, (int)(elapsed.TotalDays / 365))} 年前";
    }
}

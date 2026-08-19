using Portal.Localization;

namespace Portal.Core.App.Helpers;

public static class RelativeTime
{
    public static string Format(DateTime timestamp)
    {
        var localTime = timestamp.Kind == DateTimeKind.Utc ? timestamp.ToLocalTime() : timestamp;
        var elapsed = DateTime.Now - localTime;
        if (elapsed < TimeSpan.FromMinutes(1)) return CommonLanguageManager.Instance.relativeTime_justNow.CurrentValue();
        if (elapsed < TimeSpan.FromHours(1)) return string.Format(CommonLanguageManager.Instance.relativeTime_minutesAgo.CurrentValue(), Math.Max(1, (int)elapsed.TotalMinutes));
        if (elapsed < TimeSpan.FromDays(1)) return string.Format(CommonLanguageManager.Instance.relativeTime_hoursAgo.CurrentValue(), Math.Max(1, (int)elapsed.TotalHours));
        if (elapsed < TimeSpan.FromDays(2)) return CommonLanguageManager.Instance.relativeTime_oneDayAgo.CurrentValue();
        if (elapsed < TimeSpan.FromDays(7)) return string.Format(CommonLanguageManager.Instance.relativeTime_daysAgo.CurrentValue(), (int)elapsed.TotalDays);
        if (elapsed < TimeSpan.FromDays(14)) return CommonLanguageManager.Instance.relativeTime_oneWeekAgo.CurrentValue();
        if (elapsed < TimeSpan.FromDays(30)) return string.Format(CommonLanguageManager.Instance.relativeTime_weeksAgo.CurrentValue(), Math.Max(2, (int)(elapsed.TotalDays / 7)));
        if (elapsed < TimeSpan.FromDays(365)) return string.Format(CommonLanguageManager.Instance.relativeTime_monthsAgo.CurrentValue(), Math.Max(1, (int)(elapsed.TotalDays / 30)));
        return string.Format(CommonLanguageManager.Instance.relativeTime_yearsAgo.CurrentValue(), Math.Max(1, (int)(elapsed.TotalDays / 365)));
    }
}

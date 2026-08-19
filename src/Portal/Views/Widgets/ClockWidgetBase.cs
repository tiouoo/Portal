using Avalonia.Controls;
using Avalonia.Threading;
using Portal.Localization;
using Timer = System.Timers.Timer;

namespace Portal.Views.Widgets;

public abstract class ClockWidgetBase : IWidgetContent
{
    private TextBlock? _dateText;
    private TextBlock? _timeText;
    private TextBlock? _timeWithSecondsText;
    private Timer? _timer;
    private TextBlock? _weekText;

    protected void InitializeClock()
    {
        _timeText = this.FindControl<TextBlock>("TimeText");
        _timeWithSecondsText = this.FindControl<TextBlock>("TimeWithSecondsText");
        _dateText = this.FindControl<TextBlock>("DateText");
        _weekText = this.FindControl<TextBlock>("WeekText");
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        UpdateTime();
        _timer = new Timer(1000) { AutoReset = true };
        _timer.Elapsed += (_, _) => Dispatcher.UIThread.Post(UpdateTime);
        _timer.Start();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Dispose();
            _timer = null;
        }
    }

    private void UpdateTime()
    {
        var now = DateTime.Now;
        if (_timeText != null)
            _timeText.Text = now.ToString("HH:mm");
        if (_timeWithSecondsText != null)
            _timeWithSecondsText.Text = now.ToString("HH:mm:ss");
        if (_dateText != null)
            _dateText.Text = string.Format(CommonLanguageManager.Instance.widgets_dateFormat.CurrentValue(), now);
        if (_weekText != null)
            _weekText.Text = GetWeekday(now);
    }

    private static string GetWeekday(DateTime now)
    {
        return now.DayOfWeek switch
        {
            DayOfWeek.Monday => CommonLanguageManager.Instance.overlay_weekdayMonday.CurrentValue(),
            DayOfWeek.Tuesday => CommonLanguageManager.Instance.overlay_weekdayTuesday.CurrentValue(),
            DayOfWeek.Wednesday => CommonLanguageManager.Instance.overlay_weekdayWednesday.CurrentValue(),
            DayOfWeek.Thursday => CommonLanguageManager.Instance.overlay_weekdayThursday.CurrentValue(),
            DayOfWeek.Friday => CommonLanguageManager.Instance.overlay_weekdayFriday.CurrentValue(),
            DayOfWeek.Saturday => CommonLanguageManager.Instance.overlay_weekdaySaturday.CurrentValue(),
            _ => CommonLanguageManager.Instance.overlay_weekdaySunday.CurrentValue()
        };
    }
}
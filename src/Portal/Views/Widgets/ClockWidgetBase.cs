using System;
using System.Timers;
using Avalonia.Controls;
using Avalonia.Threading;
using Timer = System.Timers.Timer;

namespace Portal.Views.Widgets;

public abstract class ClockWidgetBase : IWidgetContent
{
    private Timer? _timer;
    private TextBlock? _timeText;
    private TextBlock? _timeWithSecondsText;
    private TextBlock? _dateText;
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
            _dateText.Text = $"{now:yyyy年M月d日}";
        if (_weekText != null)
            _weekText.Text = GetWeekday(now);
    }

    private static string GetWeekday(DateTime now) => now.DayOfWeek switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日"
    };
}

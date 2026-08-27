using System.Globalization;
using Avalonia.Controls;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Module.Widgets;
using Portal.Localization;

namespace Portal.Views.Widgets;

public partial class PlayTimeWidget : IWidgetContent
{
    public PlayTimeWidget() : this(new WidgetCellSize(2, 1))
    {
    }

    public PlayTimeWidget(WidgetCellSize size)
    {
        Size = size;
        InitializeComponent();
        DataContext = this;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Refresh();
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        InstanceManager.Instance.StatisticsChanged += OnStatisticsChanged;
        InstanceManager.Instance.InstancesChanged += OnStatisticsChanged;
        Refresh();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        InstanceManager.Instance.StatisticsChanged -= OnStatisticsChanged;
        InstanceManager.Instance.InstancesChanged -= OnStatisticsChanged;
    }

    private void OnStatisticsChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var totalSeconds = InstanceManager.Instance.Instances.Sum(instance => instance.GetTotalPlayTimeSeconds());
        var sessions = InstanceManager.Instance.Instances.Sum(instance => instance.Config?.PlaySessions ?? 0);
        var (value, unit) = FormatTime(totalSeconds);

        TitleText.Text = CommonLanguageManager.Instance.widgets_playTime.CurrentValue();
        TimeValue.Text = value;
        TimeUnit.Text = unit;
        SessionsValue.Text = sessions.ToString();
        SessionsUnit.Text = CommonLanguageManager.Instance.widgets_playTimeSessions.CurrentValue();
        Divider.IsVisible = Size.Columns > 1;
    }

    private static (string Value, string Unit) FormatTime(long seconds)
    {
        var value = seconds < 60 ? seconds : seconds < 3600 ? seconds / 60d : seconds / 3600d;
        var unit = seconds < 60 ? "s" : seconds < 3600 ? "min" : "h";
        return (value < 1000 ? value.ToString("F1", CultureInfo.InvariantCulture) : ((long)value).ToString(), unit);
    }
}

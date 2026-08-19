using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Portal.Core.Minecraft.Classes;
using Portal.Localization;

namespace Portal.Views.Pages.InstancePages;

public sealed class RecentPlayTimeChart : Control
{
    private const double LeftPadding = 52;
    private const double RightPadding = 14;
    private const double TopPadding = 14;
    private const double BottomPadding = 30;

    public static readonly StyledProperty<MinecraftInstance?> InstanceProperty =
        AvaloniaProperty.Register<RecentPlayTimeChart, MinecraftInstance?>(nameof(Instance));

    public static readonly StyledProperty<IBrush> LineBrushProperty =
        AvaloniaProperty.Register<RecentPlayTimeChart, IBrush>(nameof(LineBrush), Brushes.DodgerBlue);

    public static readonly StyledProperty<IBrush> AxisForegroundProperty =
        AvaloniaProperty.Register<RecentPlayTimeChart, IBrush>(nameof(AxisForeground), Brushes.Gray);

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<RecentPlayTimeChart, FontFamily>(nameof(FontFamily), FontFamily.Default);

    public static readonly StyledProperty<IBrush> TooltipBackgroundProperty =
        AvaloniaProperty.Register<RecentPlayTimeChart, IBrush>(nameof(TooltipBackground), Brushes.Black);

    public static readonly StyledProperty<IBrush> TooltipBorderBrushProperty =
        AvaloniaProperty.Register<RecentPlayTimeChart, IBrush>(nameof(TooltipBorderBrush), Brushes.Gray);

    public static readonly StyledProperty<IBrush> TooltipForegroundProperty =
        AvaloniaProperty.Register<RecentPlayTimeChart, IBrush>(nameof(TooltipForeground), Brushes.White);

    public static readonly StyledProperty<int> DaysProperty =
        AvaloniaProperty.Register<RecentPlayTimeChart, int>(nameof(Days), 7);

    private int? _hoveredPoint;

    public RecentPlayTimeChart()
    {
        ClipToBounds = true;
    }

    public MinecraftInstance? Instance
    {
        get => GetValue(InstanceProperty);
        set => SetValue(InstanceProperty, value);
    }

    public IBrush LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public IBrush AxisForeground
    {
        get => GetValue(AxisForegroundProperty);
        set => SetValue(AxisForegroundProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public IBrush TooltipBackground
    {
        get => GetValue(TooltipBackgroundProperty);
        set => SetValue(TooltipBackgroundProperty, value);
    }

    public IBrush TooltipBorderBrush
    {
        get => GetValue(TooltipBorderBrushProperty);
        set => SetValue(TooltipBorderBrushProperty, value);
    }

    public IBrush TooltipForeground
    {
        get => GetValue(TooltipForegroundProperty);
        set => SetValue(TooltipForegroundProperty, value);
    }

    public int Days
    {
        get => GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == InstanceProperty || change.Property == DaysProperty)
            InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;

        context.DrawRectangle(Brushes.Transparent, null, bounds);
        var graphWidth = bounds.Width - LeftPadding - RightPadding;
        var graphHeight = bounds.Height - TopPadding - BottomPadding;
        if (Instance is null || graphWidth <= 0 || graphHeight <= 0)
            return;

        var entries = Instance.GetRecentDailyPlayTime(Days);
        var maximum = entries.Max(entry => entry.Seconds);
        var yMaximum = GetAxisMaximum(maximum);
        var axisBrush = AxisForeground;
        var lineBrush = LineBrush;
        var gridBrush = new SolidColorBrush(GetColor(axisBrush, Colors.Gray), 0.16);
        var points = entries.Select((entry, index) => new Point(
            LeftPadding + graphWidth * index / (entries.Count - 1),
            TopPadding + graphHeight * (1 - (double)entry.Seconds / yMaximum))).ToArray();

        for (var tick = 0; tick <= 3; tick++)
        {
            var value = yMaximum * tick / 3;
            var y = TopPadding + graphHeight * (1 - (double)tick / 3);
            context.DrawLine(new Pen(gridBrush), new Point(LeftPadding, y), new Point(bounds.Width - RightPadding, y));
            DrawText(context, FormatDuration(value), new Point(2, y - 7), 11, axisBrush);
        }

        var labelCount = Math.Min(entries.Count, graphWidth < 300 ? 3 : 4);
        for (var label = 0; label < labelCount; label++)
        {
            var index = label * (entries.Count - 1) / (labelCount - 1);
            var date = entries[index].Date.ToString("M/d", CultureInfo.CurrentCulture);
            var text = CreateText(date, 11, axisBrush);
            context.DrawText(text, new Point(points[index].X - text.Width / 2, bounds.Height - BottomPadding + 9));
        }

        var line = new StreamGeometry();
        using (var geometry = line.Open())
        {
            geometry.BeginFigure(points[0], false);
            for (var index = 0; index < points.Length - 1; index++)
            {
                var previous = points[Math.Max(0, index - 1)];
                var current = points[index];
                var next = points[index + 1];
                var following = points[Math.Min(points.Length - 1, index + 2)];
                geometry.CubicBezierTo(
                    new Point(current.X + (next.X - previous.X) / 6, current.Y + (next.Y - previous.Y) / 6),
                    new Point(next.X - (following.X - current.X) / 6, next.Y - (following.Y - current.Y) / 6),
                    next);
            }
        }

        context.DrawGeometry(null, new Pen(lineBrush, 2.5), line);
        foreach (var point in points)
            context.DrawEllipse(Brushes.White, new Pen(lineBrush, 1.75), point, 3.5, 3.5);

        if (_hoveredPoint is not int hovered)
            return;

        var hoveredEntry = entries[hovered];
        var hoveredPosition = points[hovered];
        context.DrawLine(new Pen(new SolidColorBrush(GetColor(lineBrush, Colors.DodgerBlue), 0.4)),
            new Point(hoveredPosition.X, TopPadding), new Point(hoveredPosition.X, TopPadding + graphHeight));
        context.DrawEllipse(lineBrush, null, hoveredPosition, 5, 5);

        var tooltip = CreateText(
            string.Format(CommonLanguageManager.Instance.recentPlay_durationTooltip.CurrentValue(),
                hoveredEntry.Date.ToString("yyyy-MM-dd"), FormatDuration(hoveredEntry.Seconds)), 14,
            TooltipForeground);
        var tooltipX = Math.Clamp(hoveredPosition.X + 12, 4, bounds.Width - tooltip.Width - 12);
        var tooltipY = Math.Max(4, hoveredPosition.Y - tooltip.Height - 14);
        var tooltipBounds = new Rect(tooltipX - 7, tooltipY - 5, tooltip.Width + 14, tooltip.Height + 10);
        context.DrawRectangle(TooltipBackground, new Pen(TooltipBorderBrush), tooltipBounds, 5, 5);
        context.DrawText(tooltip, new Point(tooltipX, tooltipY));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var graphWidth = Bounds.Width - LeftPadding - RightPadding;
        if (Instance is null || graphWidth <= 0)
            return;

        var index = (int)Math.Round((e.GetPosition(this).X - LeftPadding) / graphWidth * (Days - 1));
        var hovered = Math.Clamp(index, 0, Days - 1);
        if (_hoveredPoint != hovered)
        {
            _hoveredPoint = hovered;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoveredPoint is not null)
        {
            _hoveredPoint = null;
            InvalidateVisual();
        }
    }

    private static Color GetColor(IBrush brush, Color fallback)
    {
        return brush is SolidColorBrush solidBrush ? solidBrush.Color : fallback;
    }

    private static long GetAxisMaximum(long maximum)
    {
        var targetStep = Math.Max(1, maximum / 3d);
        var candidates = new long[] { 20, 60, 120, 300, 600, 900, 1800, 3600, 7200, 14400, 28800, 43200, 86400 };
        var step = candidates.FirstOrDefault(candidate => candidate >= targetStep);
        if (step == 0)
            step = (long)Math.Ceiling(targetStep / 86400) * 86400;
        return step * 3;
    }

    private static string FormatDuration(double seconds)
    {
        return seconds < 60
            ? string.Format(CommonLanguageManager.Instance.recentPlay_seconds.CurrentValue(), seconds)
            : seconds < 3600
                ? string.Format(CommonLanguageManager.Instance.recentPlay_minutes.CurrentValue(), seconds / 60)
                : seconds < 86400
                    ? string.Format(CommonLanguageManager.Instance.recentPlay_hours.CurrentValue(), seconds / 3600)
                    : string.Format(CommonLanguageManager.Instance.recentPlay_days.CurrentValue(), seconds / 86400);
    }

    private FormattedText CreateText(string text, double size, IBrush brush)
    {
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily),
            size, brush);
    }

    private void DrawText(DrawingContext context, string text, Point point, double size, IBrush brush)
    {
        context.DrawText(CreateText(text, size, brush), point);
    }
}
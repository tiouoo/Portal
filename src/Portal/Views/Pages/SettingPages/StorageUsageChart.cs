using System.Globalization;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System.Collections.Specialized;
using System.ComponentModel;
using Tio.Avalonia.Standard.Modules.Extensions;

namespace Portal.Views.Pages.SettingPages;

public sealed class StorageUsageChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<StorageChartItem>?> ItemsProperty =
        AvaloniaProperty.Register<StorageUsageChart, IReadOnlyList<StorageChartItem>?>(nameof(Items));

    public static readonly StyledProperty<IBrush> TextBrushProperty =
        AvaloniaProperty.Register<StorageUsageChart, IBrush>(nameof(TextBrush), Brushes.White);

    public static readonly StyledProperty<IBrush> SecondaryTextBrushProperty =
        AvaloniaProperty.Register<StorageUsageChart, IBrush>(nameof(SecondaryTextBrush), Brushes.Gray);

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<StorageUsageChart, FontFamily>(nameof(FontFamily), FontFamily.Default);

    public static readonly StyledProperty<IBrush> LegendBackgroundProperty =
        AvaloniaProperty.Register<StorageUsageChart, IBrush>(nameof(LegendBackground), Brushes.Transparent);

    private readonly CubicEaseOut _ease = new();
    private readonly List<double> _displayedValues = [];
    private readonly List<double> _startValues = [];
    private readonly List<double> _hoverProgress = [];
    private const double MinimumSliceAngle = 8;
    private int? _hoveredSlice;
    private int _hoverAnimationToken;
    private int _valueAnimationToken;
    public IReadOnlyList<StorageChartItem>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public IBrush TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public IBrush SecondaryTextBrush
    {
        get => GetValue(SecondaryTextBrushProperty);
        set => SetValue(SecondaryTextBrushProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public IBrush LegendBackground
    {
        get => GetValue(LegendBackgroundProperty);
        set => SetValue(LegendBackgroundProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty)
        {
            if (change.OldValue is INotifyCollectionChanged oldCollection)
                oldCollection.CollectionChanged -= OnItemsChanged;
            if (change.NewValue is INotifyCollectionChanged newCollection)
                newCollection.CollectionChanged += OnItemsChanged;
            SubscribeItems();
            AnimateValues();
        }
        else if (change.Property == TextBrushProperty || change.Property == SecondaryTextBrushProperty ||
                 change.Property == FontFamilyProperty || change.Property == LegendBackgroundProperty)
            InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.Transparent, null, Bounds);

        var chartSize = Math.Min(Bounds.Height, Math.Min(150, Bounds.Width * 0.42));
        if (chartSize <= 0) return;

        var center = new Point(chartSize / 2, Bounds.Height / 2);
        var outerRadius = chartSize * 0.46;
        var innerRadius = chartSize * 0.25;
        var values = _displayedValues;
        var total = values.Sum();

        if (total <= 0)
        {
            context.DrawEllipse(new SolidColorBrush(Colors.Gray, 0.2), null, center, outerRadius, outerRadius);
            context.DrawEllipse(new SolidColorBrush(Colors.Transparent), null, center, innerRadius, innerRadius);
        }
        else
        {
            var sweeps = GetSliceSweeps(values);
            var startAngle = -90d;
            for (var index = 0; index < values.Count; index++)
            {
                var value = index < values.Count ? values[index] : 0;
                if (value <= 0) continue;
                var sweep = sweeps[index];
                var hover = index < _hoverProgress.Count ? _hoverProgress[index] : 0;
                var opacity = _hoveredSlice == index || (_hoveredSlice is null && hover >= 0.999)
                    ? 1
                    : 0.3 + 0.7 * hover;
                var offset = _hoveredSlice == index ? chartSize * 0.06 * hover : 0;
                var middleAngle = (startAngle + sweep / 2) * Math.PI / 180;
                var sliceCenter = new Point(center.X + Math.Cos(middleAngle) * offset,
                    center.Y + Math.Sin(middleAngle) * offset);
                DrawRingSlice(context, sliceCenter, innerRadius, outerRadius, startAngle, sweep,
                    new SolidColorBrush(Color.Parse(Items![index].Color), opacity));
                startAngle += sweep;
            }
        }

        var legendX = chartSize + 8;
        var legendWidth = Math.Max(0, Bounds.Width - legendX);
        if (Items is null) return;
        var columnGap = 28d;
        var columnWidth = Math.Max(100, (legendWidth - columnGap) / 2);
        var rowHeight = 54d;
        var rowCount = (Items.Count + 1) / 2;
        var legendTop = Bounds.Height / 2 - rowHeight * rowCount / 2;
        for (var index = 0; index < Items.Count; index++)
        {
            var value = index < values.Count ? values[index] : 0;
            var column = index % 2;
            var row = index / 2;
            DrawLegend(context, index, Items[index].Label, value, total,
                legendX + column * (columnWidth + columnGap), legendTop + row * rowHeight, columnWidth);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        var chartSize = Math.Min(Bounds.Height, Math.Min(150, Bounds.Width * 0.42));
        var center = new Point(chartSize / 2, Bounds.Height / 2);
        var distance = Math.Sqrt(Math.Pow(position.X - center.X, 2) + Math.Pow(position.Y - center.Y, 2));
        var outerRadius = chartSize * 0.46;
        var innerRadius = chartSize * 0.25;
        int? hovered = null;

        if (distance >= innerRadius - 24 && distance <= outerRadius + 24)
        {
            var angle = (Math.Atan2(position.Y - center.Y, position.X - center.X) * 180 / Math.PI + 450) % 360;
            var total = _displayedValues.Sum();
            if (total > 0)
            {
                var sweeps = GetSliceSweeps(_displayedValues);
                var accumulated = 0d;
                for (var index = 0; index < _displayedValues.Count; index++)
                {
                    accumulated += sweeps[index];
                    if (angle < accumulated)
                    {
                        hovered = index;
                        break;
                    }
                }
            }
        }
        else if (position.X >= chartSize - 2)
        {
            var legendX = chartSize + 8;
            var columnGap = 28d;
            var columnWidth = Math.Max(100, (Bounds.Width - legendX - columnGap) / 2);
            var rowHeight = 54d;
            var rowCount = (_displayedValues.Count + 1) / 2;
            var legendTop = Bounds.Height / 2 - rowHeight * rowCount / 2;
            var column = (int)Math.Floor((position.X - legendX) / (columnWidth + columnGap));
            var row = (int)Math.Floor((position.Y - legendTop + 8) / rowHeight);
            if (column is >= 0 and <= 1 && row >= 0)
            {
                var index = row * 2 + column;
                var cellX = legendX + column * (columnWidth + columnGap);
                var cellY = legendTop + row * rowHeight;
                if (position.X >= cellX && position.X <= cellX + columnWidth &&
                    position.Y >= cellY - 4 && position.Y <= cellY + 44 &&
                    index < _displayedValues.Count)
                    hovered = index;
            }
        }

        if (hovered is null && _hoveredSlice is not null &&
            distance >= innerRadius - 20 && distance <= outerRadius + 20)
            hovered = _hoveredSlice;
        SetHoveredSlice(hovered);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHoveredSlice(null);
    }

    private void AnimateValues()
    {
        var items = Items ?? [];
        _startValues.Clear();
        _startValues.AddRange(_displayedValues);
        _displayedValues.AddRange(Enumerable.Repeat(0d, Math.Max(0, items.Count - _displayedValues.Count)));
        while (_displayedValues.Count > items.Count) _displayedValues.RemoveAt(_displayedValues.Count - 1);
        while (_startValues.Count < items.Count) _startValues.Add(0);
        var token = ++_valueAnimationToken;
        StartAnimation(token, () => _valueAnimationToken, TimeSpan.FromMilliseconds(420), progress =>
        {
            for (var index = 0; index < items.Count; index++)
                _displayedValues[index] = Lerp(_startValues[index], items[index].SizeBytes, progress);
        });
    }

    private void SetHoveredSlice(int? hovered)
    {
        if (_hoveredSlice == hovered) return;
        _hoveredSlice = hovered;
        var previous = _hoverProgress.ToArray();
        _hoverProgress.Clear();
        var starts = previous.Length == _displayedValues.Count
            ? previous
            : Enumerable.Repeat(1d, _displayedValues.Count).ToArray();
        _hoverProgress.AddRange(starts);
        var token = ++_hoverAnimationToken;
        StartAnimation(token, () => _hoverAnimationToken, TimeSpan.FromMilliseconds(150), progress =>
        {
            for (var index = 0; index < _hoverProgress.Count; index++)
                _hoverProgress[index] = Lerp(starts[index], hovered == index ? 1 : 0, progress);
        });
    }

    private void StartAnimation(int token, Func<int> currentToken, TimeSpan duration, Action<double> apply)
    {
        if (TopLevel.GetTopLevel(this) is null)
        {
            apply(1);
            InvalidateVisual();
            return;
        }

        TimeSpan? start = null;
        RequestFrame(Frame);
        return;

        void Frame(TimeSpan now)
        {
            if (token != currentToken()) return;
            start ??= now;
            var progress = Math.Clamp((now - start.Value).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            apply(_ease.Ease(progress));
            InvalidateVisual();
            if (progress < 1) RequestFrame(Frame);
        }
    }

    private void DrawLegend(DrawingContext context, int index, string label, double value, double total,
        double x, double y, double width)
    {
        var hover = index < _hoverProgress.Count ? _hoverProgress[index] : 0;
        var opacity = _hoveredSlice == index || (_hoveredSlice is null && hover >= 0.999)
            ? 1
            : 0.3 + 0.7 * hover;
        var color = Color.Parse(Items![index].Color);
        var backgroundOpacity = _hoveredSlice == index ? 0.2 : 0.08;
        var backgroundBounds = new Rect(x - 6, y - 4, width, 48);
        context.DrawRectangle(LegendBackground, null, backgroundBounds, 6, 6);
        context.DrawRectangle(new SolidColorBrush(color, backgroundOpacity), null, backgroundBounds, 6, 6);
        context.DrawEllipse(new SolidColorBrush(color, opacity), null, new Point(x + 5, y + 9), 5, 5);

        var labelText = CreateText(label, 14, TextBrush, FontWeight.SemiBold);
        context.DrawText(labelText, new Point(x + 18, y));
        var sizeText = CreateText(((long)value).ToHumanReadableSize(1), 12, SecondaryTextBrush);
        context.DrawText(sizeText, new Point(x + 18, y + 22));
        var percentText = CreateText(total > 0 ? $"{value / total:P1}" : "0.0%", 12, SecondaryTextBrush);
        context.DrawText(percentText, new Point(Math.Max(x + 18, x + width - percentText.Width), y + 22));
    }

    private void SubscribeItems()
    {
        foreach (var item in Items ?? []) item.PropertyChanged -= OnItemChanged;
        foreach (var item in Items ?? []) item.PropertyChanged += OnItemChanged;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SubscribeItems();
        AnimateValues();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StorageChartItem.SizeBytes)) AnimateValues();
    }

    private FormattedText CreateText(string text, double size, IBrush brush, FontWeight? weight = null)
    {
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle.Normal, weight ?? FontWeight.Normal), size, brush);
    }

    private void RequestFrame(Action<TimeSpan> callback)
    {
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(callback);
    }

    private static void DrawRingSlice(DrawingContext context, Point center, double innerRadius, double outerRadius,
        double startAngle, double sweepAngle, IBrush brush)
    {
        if (sweepAngle >= 359.999)
        {
            context.DrawEllipse(null, new Pen(brush, outerRadius - innerRadius), center,
                (outerRadius + innerRadius) / 2, (outerRadius + innerRadius) / 2);
            return;
        }

        var sliceGeometry = new StreamGeometry();
        using (var path = sliceGeometry.Open())
        {
            var outerStart = PointOnCircle(center, outerRadius, startAngle);
            var outerEnd = PointOnCircle(center, outerRadius, startAngle + sweepAngle);
            var innerEnd = PointOnCircle(center, innerRadius, startAngle + sweepAngle);
            var innerStart = PointOnCircle(center, innerRadius, startAngle);
            path.BeginFigure(outerStart, true);
            path.ArcTo(outerEnd, new Size(outerRadius, outerRadius), 0, sweepAngle > 180,
                SweepDirection.Clockwise);
            path.LineTo(innerEnd);
            path.ArcTo(innerStart, new Size(innerRadius, innerRadius), 0, sweepAngle > 180,
                SweepDirection.CounterClockwise);
            path.EndFigure(true);
        }
        context.DrawGeometry(brush, null, sliceGeometry);
    }

    private static double[] GetSliceSweeps(IReadOnlyList<double> values)
    {
        var positiveValues = values.Select(value => Math.Max(0, value)).ToArray();
        var total = positiveValues.Sum();
        if (total <= 0) return new double[values.Count];

        var positiveCount = positiveValues.Count(value => value > 0);
        var minimumAngle = Math.Min(MinimumSliceAngle, 360d / positiveCount);
        var remainingAngle = 360 - minimumAngle * positiveCount;
        var remainingValue = positiveValues.Sum(value => Math.Max(0, value - total * minimumAngle / 360));

        return positiveValues.Select(value =>
        {
            if (value <= 0) return 0;
            var proportionalAngle = remainingValue > 0
                ? Math.Max(0, value - total * minimumAngle / 360) / remainingValue * remainingAngle
                : 0;
            return minimumAngle + proportionalAngle;
        }).ToArray();
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private static double Lerp(double from, double to, double progress) => from + (to - from) * progress;
}

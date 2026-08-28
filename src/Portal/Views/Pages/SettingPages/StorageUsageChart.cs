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

    private readonly CubicEaseOut _ease = new();
    private readonly List<double> _displayedValues = [];
    private readonly List<double> _startValues = [];
    private readonly List<double> _hoverProgress = [];
    private const double MinimumSliceAngle = 8;
    private const double LegendGap = 35;
    private const double LegendRightPadding = 32;
    private const double LegendColumnGap = 64;
    private const double LegendRowHeight = 54;
    private const double LegendContentHeight = 36;
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
                 change.Property == FontFamilyProperty)
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
            var selectionProgress = _hoverProgress.Count > 0 ? _hoverProgress.Max() : 0;
            for (var index = 0; index < values.Count; index++)
            {
                var value = index < values.Count ? values[index] : 0;
                if (value <= 0) continue;
                var sweep = sweeps[index];
                var hover = index < _hoverProgress.Count ? _hoverProgress[index] : 0;
                var opacity = _hoveredSlice is null || _hoveredSlice == index
                    ? 1
                    : 1 - 0.7 * selectionProgress;
                var highlightedOuterRadius = outerRadius +
                                             (_hoveredSlice == index ? chartSize * 0.06 * hover : 0);
                DrawRingSlice(context, center, innerRadius, highlightedOuterRadius, startAngle, sweep,
                    new SolidColorBrush(Color.Parse(Items![index].Color), opacity));
                startAngle += sweep;
            }
        }

        var legendX = chartSize + LegendGap;
        var legendWidth = Math.Max(0, Bounds.Width - legendX - LegendRightPadding);
        if (Items is null) return;
        var columnWidth = Math.Max(100, (legendWidth - LegendColumnGap) / 2);
        var rowCount = (Items.Count + 1) / 2;
        var legendHeight = Math.Max(0, (rowCount - 1) * LegendRowHeight + LegendContentHeight);
        var legendTop = (Bounds.Height - legendHeight) / 2;
        for (var index = 0; index < Items.Count; index++)
        {
            var value = index < values.Count ? values[index] : 0;
            var column = index % 2;
            var row = index / 2;
            var itemX = legendX + column * (columnWidth + LegendColumnGap);
            var itemY = legendTop + row * LegendRowHeight;
            context.DrawRectangle(Brushes.Transparent, null, GetLegendHitBounds(itemX, itemY, columnWidth));
            DrawLegend(context, index, Items[index].Label, value, total, itemX, itemY);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        var chartSize = Math.Min(Bounds.Height, Math.Min(150, Bounds.Width * 0.42));
        int? hovered = null;

        if (position.X >= chartSize + LegendGap - 6)
        {
            var legendX = chartSize + LegendGap;
            var legendWidth = Math.Max(0, Bounds.Width - legendX - LegendRightPadding);
            var columnWidth = Math.Max(100, (legendWidth - LegendColumnGap) / 2);
            var rowCount = (_displayedValues.Count + 1) / 2;
            var legendHeight = Math.Max(0, (rowCount - 1) * LegendRowHeight + LegendContentHeight);
            var legendTop = (Bounds.Height - legendHeight) / 2;
            for (var index = 0; index < _displayedValues.Count; index++)
            {
                var column = index % 2;
                var row = index / 2;
                var cellX = legendX + column * (columnWidth + LegendColumnGap);
                var cellY = legendTop + row * LegendRowHeight;
                if (GetLegendHitBounds(cellX, cellY, columnWidth).Contains(position))
                {
                    hovered = index;
                    break;
                }
            }
        }

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
            : Enumerable.Repeat(0d, _displayedValues.Count).ToArray();
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
        double x, double y)
    {
        var selectionProgress = _hoverProgress.Count > 0 ? _hoverProgress.Max() : 0;
        var opacity = _hoveredSlice is null || _hoveredSlice == index
            ? 1
            : 1 - 0.7 * selectionProgress;
        var color = Color.Parse(Items![index].Color);
        context.DrawEllipse(new SolidColorBrush(color, opacity), null, new Point(x + 5, y + 8), 5, 5);

        var labelText = CreateText(label, 14, TextBrush, FontWeight.SemiBold);
        context.DrawText(labelText, new Point(x + 18, y));
        var sizeText = ((long)value).ToHumanReadableSize(1);
        var percentage = total > 0 ? value / total * 100 : 0;
        var usageText = CreateText($"{sizeText}·{percentage:0.0} %", 12, SecondaryTextBrush);
        context.DrawText(usageText, new Point(x + 18, y + 22));
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
        var sweeps = new double[values.Count];
        var remainingIndices = positiveValues
            .Select((value, index) => (value, index))
            .Where(item => item.value > 0)
            .Select(item => item.index)
            .ToList();
        var remainingAngle = 360d;
        var remainingValue = total;

        while (remainingIndices.Count > 0)
        {
            var constrainedIndices = remainingIndices
                .Where(index => positiveValues[index] / remainingValue * remainingAngle < minimumAngle)
                .ToArray();

            if (constrainedIndices.Length == 0)
            {
                foreach (var index in remainingIndices)
                    sweeps[index] = positiveValues[index] / remainingValue * remainingAngle;
                break;
            }

            foreach (var index in constrainedIndices)
            {
                sweeps[index] = minimumAngle;
                remainingAngle -= minimumAngle;
                remainingValue -= positiveValues[index];
                remainingIndices.Remove(index);
            }
        }

        return sweeps;
    }

    private static Rect GetLegendHitBounds(double x, double y, double width) =>
        new(x - 6, y - (LegendRowHeight - LegendContentHeight) / 2, width + 12, LegendRowHeight);

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private static double Lerp(double from, double to, double progress) => from + (to - from) * progress;
}

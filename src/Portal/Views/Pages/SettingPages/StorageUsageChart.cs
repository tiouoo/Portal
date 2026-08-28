using System.Globalization;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using SkiaSharp;
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
    private WriteableBitmap? _chartBitmap;
    private const double MinimumSliceAngle = 8;
    private const int ChartSupersampling = 4;
    private const double SliceOverlapDip = 1;
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

        DrawChart(context, chartSize, center.Y, innerRadius, outerRadius, values, total);

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

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _chartBitmap?.Dispose();
        _chartBitmap = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void DrawChart(DrawingContext context, double chartSize, double centerY, double innerRadius,
        double outerRadius, IReadOnlyList<double> values, double total)
    {
        var padding = chartSize * 0.06 + 2;
        var logicalSize = chartSize + padding * 2;
        var pixelSize = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(logicalSize * ChartSupersampling)),
            Math.Max(1, (int)Math.Ceiling(logicalSize * ChartSupersampling)));

        if (_chartBitmap?.PixelSize != pixelSize)
        {
            _chartBitmap?.Dispose();
            _chartBitmap = new WriteableBitmap(pixelSize,
                new Vector(96 * ChartSupersampling, 96 * ChartSupersampling),
                PixelFormat.Bgra8888, AlphaFormat.Premul);
        }

        // One supersampled annulus clip gives every slice identical inner and outer antialiased edges.
        using (var framebuffer = _chartBitmap.Lock())
        using (var surface = SKSurface.Create(
                   new SKImageInfo(framebuffer.Size.Width, framebuffer.Size.Height, SKColorType.Bgra8888,
                       SKAlphaType.Premul), framebuffer.Address, framebuffer.RowBytes))
        {
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(ChartSupersampling);

            var center = new SKPoint((float)(padding + chartSize / 2), (float)(padding + chartSize / 2));
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                BlendMode = SKBlendMode.Src
            };
            using var ringClip = CreateRingPath(center, (float)innerRadius, (float)outerRadius);

            canvas.Save();
            canvas.ClipPath(ringClip, SKClipOperation.Intersect, true);

            if (total <= 0)
            {
                paint.Color = new SKColor(128, 128, 128, 51);
                canvas.DrawCircle(center, (float)(outerRadius + 1), paint);
            }
            else
            {
                DrawSlices(canvas, paint, center, (float)outerRadius, values);
            }

            canvas.Restore();

            if (total > 0 && _hoveredSlice is { } hoveredIndex && hoveredIndex < values.Count &&
                values[hoveredIndex] > 0)
            {
                DrawHoveredSlice(canvas, paint, center, chartSize, innerRadius, outerRadius, values, hoveredIndex);
            }

            surface.Flush();
        }

        var bitmapLogicalSize = new Size(
            _chartBitmap.PixelSize.Width / (double)ChartSupersampling,
            _chartBitmap.PixelSize.Height / (double)ChartSupersampling);
        var bitmapPixelBounds = new Rect(
            0, 0, _chartBitmap.PixelSize.Width, _chartBitmap.PixelSize.Height);
        var destination = new Rect(-padding, centerY - chartSize / 2 - padding,
            bitmapLogicalSize.Width, bitmapLogicalSize.Height);
        using (context.PushRenderOptions(new RenderOptions
               {
                   EdgeMode = EdgeMode.Antialias,
                   BitmapInterpolationMode = BitmapInterpolationMode.HighQuality
               }))
            context.DrawImage(_chartBitmap, bitmapPixelBounds, destination);
    }

    private void DrawSlices(SKCanvas canvas, SKPaint paint, SKPoint center, float outerRadius,
        IReadOnlyList<double> values)
    {
        var sweeps = GetSliceSweeps(values);
        var lastVisibleIndex = Enumerable.Range(0, values.Count).Last(index => values[index] > 0);
        var overlapAngle = GetOverlapAngle(outerRadius);
        var selectionProgress = _hoverProgress.Count > 0 ? _hoverProgress.Max() : 0;
        var startAngle = -90d;

        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] <= 0) continue;

            var endAngle = index == lastVisibleIndex ? 270d : startAngle + sweeps[index];
            var opacity = _hoveredSlice is null || _hoveredSlice == index
                ? 1
                : 1 - 0.7 * selectionProgress;
            paint.Color = ToSkColor(Color.Parse(Items![index].Color), opacity);
            DrawWedge(canvas, paint, center, outerRadius + 1,
                startAngle - overlapAngle / 2, endAngle - startAngle + overlapAngle);
            startAngle = endAngle;
        }
    }

    private void DrawHoveredSlice(SKCanvas canvas, SKPaint paint, SKPoint center, double chartSize,
        double innerRadius, double outerRadius, IReadOnlyList<double> values, int hoveredIndex)
    {
        var hover = hoveredIndex < _hoverProgress.Count ? _hoverProgress[hoveredIndex] : 0;
        var highlightedRadius = outerRadius + chartSize * 0.06 * hover;

        var sweeps = GetSliceSweeps(values);
        var lastVisibleIndex = Enumerable.Range(0, values.Count).Last(index => values[index] > 0);
        var startAngle = -90d;
        for (var index = 0; index < hoveredIndex; index++) startAngle += sweeps[index];
        var endAngle = hoveredIndex == lastVisibleIndex ? 270d : startAngle + sweeps[hoveredIndex];
        var overlapAngle = GetOverlapAngle((float)outerRadius);

        using var highlightedClip = CreateRingPath(center, (float)innerRadius, (float)highlightedRadius);
        canvas.Save();
        canvas.ClipPath(highlightedClip, SKClipOperation.Intersect, true);
        paint.BlendMode = SKBlendMode.SrcOver;
        paint.Color = ToSkColor(Color.Parse(Items![hoveredIndex].Color), 1);
        DrawWedge(canvas, paint, center, (float)(highlightedRadius + 1),
            startAngle - overlapAngle / 2, endAngle - startAngle + overlapAngle);
        canvas.Restore();
    }

    private static SKPath CreateRingPath(SKPoint center, float innerRadius, float outerRadius)
    {
        using var builder = new SKPathBuilder { FillType = SKPathFillType.EvenOdd };
        builder.AddCircle(center.X, center.Y, outerRadius, SKPathDirection.Clockwise);
        builder.AddCircle(center.X, center.Y, innerRadius, SKPathDirection.Clockwise);
        return builder.Detach();
    }

    private static void DrawWedge(SKCanvas canvas, SKPaint paint, SKPoint center, float radius,
        double startAngle, double sweepAngle)
    {
        if (sweepAngle >= 359.999)
        {
            canvas.DrawCircle(center, radius, paint);
            return;
        }

        using var builder = new SKPathBuilder();
        builder.MoveTo(center);
        var bounds = new SKRect(center.X - radius, center.Y - radius, center.X + radius, center.Y + radius);
        builder.ArcTo(bounds, (float)startAngle, (float)sweepAngle, false);
        builder.Close();
        using var path = builder.Detach();
        canvas.DrawPath(path, paint);
    }

    private static SKColor ToSkColor(Color color, double opacity) =>
        new(color.R, color.G, color.B, (byte)Math.Round(color.A * Math.Clamp(opacity, 0, 1)));

    private static double GetOverlapAngle(float outerRadius) =>
        Math.Min(2, SliceOverlapDip / Math.Max(1, outerRadius) * 180 / Math.PI);

    private static double[] GetSliceSweeps(IReadOnlyList<double> values)
    {
        var positiveValues = values.Select(value => Math.Max(0, value)).ToArray();
        var total = positiveValues.Sum();
        if (total <= 0) return new double[values.Count];

        var positiveCount = positiveValues.Count(value => value > 0);
        var minimumAngle = Math.Min(MinimumSliceAngle, 360d / positiveCount);
        var proportionalAngle = 360d - minimumAngle * positiveCount;

        return positiveValues
            .Select(value => value > 0 ? minimumAngle + value / total * proportionalAngle : 0)
            .ToArray();
    }

    private static Rect GetLegendHitBounds(double x, double y, double width) =>
        new(x - 6, y - (LegendRowHeight - LegendContentHeight) / 2, width + 12, LegendRowHeight);

    private static double Lerp(double from, double to, double progress) => from + (to - from) * progress;
}

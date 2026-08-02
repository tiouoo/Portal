using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Portal.Classes.Entries;
using Portal.Const;
using Portal.Module.Widgets;

namespace Portal.Views.Widgets;

public partial class WidgetHost : UserControl
{
    public event Action<WidgetHost>? Resized;
    public event EventHandler<PointerPressedEventArgs>? RightButtonPressed;

    public WidgetLayoutData Layout { get; set; } = new();

    private IWidgetContent? _widgetContent;

    public IWidgetContent? WidgetContent
    {
        get => _widgetContent;
        set
        {
            _widgetContent = value;
            if (ContentHost != null)
                ContentHost.Content = value;
        }
    }

    private Border? _card;
    private Border? _resizeHandle;
    private PathIcon? _resizeIcon;
    private Point _startMousePoint;
    private Size _startSize;
    private bool _isResizing;
    private IPointer? _activePointer;

    public WidgetHost()
    {
        InitializeComponent();
        _card = this.FindControl<Border>("Card");
        _resizeHandle = this.FindControl<Border>("ResizeHandle");
        _resizeIcon = this.FindControl<PathIcon>("ResizeIcon");

        if (_resizeHandle != null)
        {
            _resizeHandle.PointerPressed += OnHandlePointerPressed;
            _resizeHandle.PointerReleased += OnHandlePointerReleased;
            _resizeHandle.PointerEntered += (_, _) => SetResizeIcon(true);
            _resizeHandle.PointerExited += (_, _) => SetResizeIcon(false);
        }

        PointerMoved += OnGlobalPointerMoved;
        PointerReleased += OnGlobalPointerReleased;
        PointerPressed += OnPointerPressed;
        ApplyBackground();

        Loaded += (sender, args) =>
        {
            var a = ContentHost.Content;
            ContentHost.Content = null;
            ContentHost.Content = a;
        };
    }

    private void SetResizeIcon(bool visible)
    {
        if (_resizeIcon != null)
            _resizeIcon.Opacity = visible ? 1 : 0;
    }

    public bool IsResizeHandleArea(Visual visual) =>
        _resizeHandle != null && (_resizeHandle == visual || _resizeHandle.IsVisualAncestorOf(visual));

    public void UpdateSizeConstraints()
    {
        var definition = WidgetRegistry.Get(Layout.Kind);
        if (definition?.SupportedSizes.Count is not > 0)
            return;

        WidgetCellSize minSize = definition.SupportedSizes[0];
        WidgetCellSize maxSize = definition.SupportedSizes[0];
        double minArea = double.MaxValue;
        double maxArea = 0;
        foreach (var size in definition.SupportedSizes)
        {
            var dims = WidgetGeometry.GetSize(size);
            double area = dims.Width * dims.Height;
            if (area < minArea)
            {
                minArea = area;
                minSize = size;
            }

            if (area > maxArea)
            {
                maxArea = area;
                maxSize = size;
            }
        }

        var minDims = WidgetGeometry.GetSize(minSize);
        var maxDims = WidgetGeometry.GetSize(maxSize);
        MinWidth = minDims.Width;
        MinHeight = minDims.Height;
        MaxWidth = maxDims.Width;
        MaxHeight = maxDims.Height;
    }
    
    public void SetSize(WidgetCellSize target)
    {
        var definition = WidgetRegistry.Get(Layout.Kind);
        if (definition == null)
            return;

        var targetDims = WidgetGeometry.GetSize(target);
        var nearest = definition.NearestSize(targetDims.Width, targetDims.Height);
        var dims = WidgetGeometry.GetSize(nearest);

        Width = dims.Width;
        Height = dims.Height;
        Layout.Size = nearest;

        if (WidgetContent?.Size != nearest)
        {
            WidgetContent = definition.Create(nearest);
            UpdateSizeConstraints();
        }
    }

    public void SnapToNearestSize()
    {
        var definition = WidgetRegistry.Get(Layout.Kind);
        if (definition == null)
            return;

        double currentW = double.IsNaN(Width) ? Bounds.Width : Width;
        double currentH = double.IsNaN(Height) ? Bounds.Height : Height;
        var nearest = definition.NearestSize(currentW, currentH);
        SetSize(nearest);
    }

    public void ApplyBackground()
    {
        if (_card == null)
            return;

        var show = Layout.ShowBackground ?? Data.ConfigEntry.ShowWidgetBackground;
        _card.IsVisible = show;
        _card.BorderThickness = show ? new Thickness(1) : new Thickness(0);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            RightButtonPressed?.Invoke(this, e);
            e.Handled = true;
        }
    }

    private void OnHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        e.Handled = true;
        _isResizing = true;
        _activePointer = e.Pointer;

        _startMousePoint = e.GetPosition(this);
        _startSize = new Size(
            double.IsNaN(Width) ? Bounds.Width : Width,
            double.IsNaN(Height) ? Bounds.Height : Height);

        Transitions = null;
        e.Pointer.Capture(_resizeHandle);
    }

    private void OnHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isResizing && e.Pointer == _activePointer)
            StopResize(e.Pointer);
    }

    private void OnGlobalPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isResizing && e.Pointer == _activePointer)
            StopResize(e.Pointer);
    }

    private void OnGlobalPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizing || e.Pointer != _activePointer)
            return;

        var currentMousePoint = e.GetPosition(this);
        double deltaX = currentMousePoint.X - _startMousePoint.X;
        double deltaY = currentMousePoint.Y - _startMousePoint.Y;

        double newWidth = Math.Clamp(_startSize.Width + deltaX, MinWidth, MaxWidth);
        double newHeight = Math.Clamp(_startSize.Height + deltaY, MinHeight, MaxHeight);

        Width = newWidth;
        Height = newHeight;
    }

    private void StopResize(IPointer pointer)
    {
        if (!_isResizing)
            return;

        _isResizing = false;
        _activePointer = null;
        pointer.Capture(null);

        Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = WidthProperty,
                Duration = TimeSpan.FromMilliseconds(200),
                Easing = new ExponentialEaseOut()
            },
            new DoubleTransition
            {
                Property = HeightProperty,
                Duration = TimeSpan.FromMilliseconds(200),
                Easing = new ExponentialEaseOut()
            }
        };

        SnapToNearestSize();
        Resized?.Invoke(this);
    }
}

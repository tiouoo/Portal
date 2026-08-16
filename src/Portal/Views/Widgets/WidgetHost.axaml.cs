using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Portal.Core.Classes.Entries;
using Portal.Core.Const;
using Portal.Core.Module.Widgets;
using Portal.Module;

namespace Portal.Views.Widgets;

public partial class WidgetHost : UserControl
{
    private readonly Border? _card;
    private readonly Border? _resizeHandle;
    private readonly PathIcon? _resizeIcon;
    private IPointer? _activePointer;
    private bool _isResizing;
    private Point _startMousePoint;
    private Size _startSize;

    private IWidgetContent? _widgetContent;

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

    public WidgetLayoutData Layout { get; set; } = new();

    public IWidgetContent? WidgetContent
    {
        get => _widgetContent;
        set
        {
            _widgetContent = value;
            if (_widgetContent != null)
                _widgetContent.Initialize(Layout);
            if (ContentHost != null)
                ContentHost.Content = value;
        }
    }

    public event Action<WidgetHost>? Resized;
    public event EventHandler<PointerPressedEventArgs>? RightButtonPressed;

    private void SetResizeIcon(bool visible)
    {
        if (_resizeIcon != null)
            _resizeIcon.Opacity = visible ? 1 : 0;
    }

    public bool IsResizeHandleArea(Visual visual)
    {
        return _resizeHandle != null && (_resizeHandle == visual || _resizeHandle.IsVisualAncestorOf(visual));
    }

    public void UpdateSizeConstraints()
    {
        var definition = WidgetRegistry.Get(Layout.Kind);
        if (definition?.SupportedSizes.Count is not > 0)
            return;


        double minWidth = double.MaxValue, minHeight = double.MaxValue;
        double maxWidth = 0, maxHeight = 0;
        foreach (var size in definition.SupportedSizes)
        {
            var dims = WidgetGeometry.GetSize(size);
            if (dims.Width < minWidth) minWidth = dims.Width;
            if (dims.Height < minHeight) minHeight = dims.Height;
            if (dims.Width > maxWidth) maxWidth = dims.Width;
            if (dims.Height > maxHeight) maxHeight = dims.Height;
        }

        MinWidth = minWidth;
        MinHeight = minHeight;
        MaxWidth = maxWidth;
        MaxHeight = maxHeight;
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

        var currentW = double.IsNaN(Width) ? Bounds.Width : Width;
        var currentH = double.IsNaN(Height) ? Bounds.Height : Height;
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
        var deltaX = currentMousePoint.X - _startMousePoint.X;
        var deltaY = currentMousePoint.Y - _startMousePoint.Y;

        var newWidth = Math.Clamp(_startSize.Width + deltaX, MinWidth, MaxWidth);
        var newHeight = Math.Clamp(_startSize.Height + deltaY, MinHeight, MaxHeight);

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
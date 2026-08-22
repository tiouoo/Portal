using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Portal.Core.Classes.Entries;
using Portal.Core.Const;
using Portal.Core.Module.Widgets;
using Portal.Localization;
using Portal.Module;
using Portal.ViewModels;

namespace Portal.Views.Widgets;

public class WidgetWorkspace : UserControl
{
    private const double DragThreshold = 6;
    private readonly List<WidgetHost> _allWidgets = [];

    private readonly Canvas? _canvas;

    private readonly Dictionary<Point, WidgetHost> _occupiedGridCells = [];
    private WidgetHost? _contextMenuWidget;
    private Point _dragStartPoint;

    private WidgetHost? _draggingWidget;
    private ContextMenu? _emptyContextMenu;

    private Border? _ghostPlaceholder;

    private WidgetHost? _pendingDragHost;
    private Point _pendingInitialPosition;
    private Point _pendingStartPoint;

    private ContextMenu? _widgetContextMenu;
    private Point _widgetInitialPosition;

    public WidgetWorkspace()
    {
        _canvas = new Canvas
        {
            Background = new SolidColorBrush(Colors.Transparent)
        };
        Content = _canvas;
        ClipToBounds = true;

        _canvas.PointerPressed += OnCanvasPointerPressed;
        AddHandler(PointerPressedEvent, OnWorkspacePointerPressed, RoutingStrategies.Bubble,
            true);
        PointerMoved += OnWorkspacePointerMoved;
        PointerReleased += OnWorkspacePointerReleased;
        PointerCaptureLost += OnWorkspacePointerCaptureLost;
        SizeChanged += OnWorkspaceSizeChanged;
        InitializeContextMenus();
        Loaded += (_, _) => LoadLayoutFromConfig();
        UpdateCanvasSize();
    }

    public event EventHandler? AddWidgetCallOn;

    private void OnWorkspaceSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateCanvasSize();
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            _contextMenuWidget = null;
            _emptyContextMenu?.Open(_canvas);
            e.Handled = true;
        }
    }

    private void InitializeContextMenus()
    {
        _widgetContextMenu = new ContextMenu();

        var deleteItem = new MenuItem
        {
            Header = CommonLanguageManager.Instance.widgets_deleteWidget.CurrentValue(), Icon = IconResources.CreateIcon("\ue640", 16)
        };
        deleteItem.Click += OnDeleteWidgetClick;
        _widgetContextMenu.Items.Add(deleteItem);

        var backgroundMenu = new MenuItem
        {
            Header = CommonLanguageManager.Instance.widgets_background.CurrentValue(), Icon = IconResources.CreateIcon("\ue646", 16)
        };
        var followItem = new MenuItem { Header = CommonLanguageManager.Instance.widgets_backgroundFollow.CurrentValue(), Classes = { "hide-icon" } };
        followItem.Click += (_, _) => SetBackgroundOverride(_contextMenuWidget, null);
        var showItem = new MenuItem { Header = CommonLanguageManager.Instance.widgets_backgroundShow.CurrentValue(), Classes = { "hide-icon" } };
        showItem.Click += (_, _) => SetBackgroundOverride(_contextMenuWidget, true);
        var hideItem = new MenuItem { Header = CommonLanguageManager.Instance.widgets_backgroundHide.CurrentValue(), Classes = { "hide-icon" } };
        hideItem.Click += (_, _) => SetBackgroundOverride(_contextMenuWidget, false);
        backgroundMenu.Items.Add(followItem);
        backgroundMenu.Items.Add(showItem);
        backgroundMenu.Items.Add(hideItem);
        _widgetContextMenu.Items.Add(backgroundMenu);

        var memoryModeItem = new MenuItem
        {
            Header = CommonLanguageManager.Instance.widgets_toggleDisplayMode.CurrentValue(),
            Icon = IconResources.CreateIcon("\ue63c", 16),
            IsVisible = false
        };
        memoryModeItem.Click += (_, _) =>
        {
            if (_contextMenuWidget?.WidgetContent is MemoryResourceWidget mem)
            {
                mem.ToggleDisplayMode();
                SaveLayout();
            }
        };
        _widgetContextMenu.Items.Add(memoryModeItem);


        var newsFilterMenu = new MenuItem
        {
            Header = CommonLanguageManager.Instance.widgets_newsFilter.CurrentValue(),
            Icon = IconResources.CreateIcon("\ue63d", 16),
            IsVisible = false
        };
        var newsAllItem = new MenuItem { Header = CommonLanguageManager.Instance.news_filterAll.CurrentValue(), Classes = { "hide-icon" } };
        newsAllItem.Click += (_, _) => SetNewsFilter(NewsFilterType.All);
        var newsJavaItem = new MenuItem { Header = CommonLanguageManager.Instance.widgets_newsJavaOnly.CurrentValue(), Classes = { "hide-icon" } };
        newsJavaItem.Click += (_, _) => SetNewsFilter(NewsFilterType.Java);
        var newsBedrockItem = new MenuItem { Header = CommonLanguageManager.Instance.widgets_newsBedrockOnly.CurrentValue(), Classes = { "hide-icon" } };
        newsBedrockItem.Click += (_, _) => SetNewsFilter(NewsFilterType.Bedrock);
        newsFilterMenu.Items.Add(newsAllItem);
        newsFilterMenu.Items.Add(newsJavaItem);
        newsFilterMenu.Items.Add(newsBedrockItem);
        _widgetContextMenu.Items.Add(newsFilterMenu);

        var imageChangeItem = new MenuItem
        {
            Header = CommonLanguageManager.Instance.widgets_changeImage.CurrentValue(),
            Icon = IconResources.CreateIcon("\ue635", 16),
            IsVisible = false
        };
        imageChangeItem.Click += (_, _) => _ = ChangeImageAsync();
        _widgetContextMenu.Items.Add(imageChangeItem);

        var imageStretchItem = new MenuItem
        {
            Header = CommonLanguageManager.Instance.widgets_toggleStretchMode.CurrentValue(),
            Icon = IconResources.CreateIcon("\ue64a", 16),
            IsVisible = false
        };
        imageStretchItem.Click += (_, _) =>
        {
            if (_contextMenuWidget?.WidgetContent is ImageViewWidget img)
            {
                img.ToggleStretchMode();
                SaveLayout();
            }
        };
        _widgetContextMenu.Items.Add(imageStretchItem);

        var sizeMenu = new MenuItem
        {
            Header = CommonLanguageManager.Instance.widgets_toggleSize.CurrentValue(), Icon = IconResources.CreateIcon("\ue64a", 16)
        };
        _widgetContextMenu.Items.Add(sizeMenu);

        _widgetContextMenu.Opened += (_, _) =>
        {
            if (_contextMenuWidget == null)
                return;

            var value = _contextMenuWidget.Layout.ShowBackground;
            followItem.IsChecked = value == null;
            showItem.IsChecked = value == true;
            hideItem.IsChecked = value == false;

            memoryModeItem.IsVisible = _contextMenuWidget.WidgetContent is MemoryResourceWidget;
            var isImage = _contextMenuWidget.WidgetContent is ImageViewWidget;
            imageChangeItem.IsVisible = isImage;
            imageStretchItem.IsVisible = isImage;

            var newsWidget = _contextMenuWidget.WidgetContent as NewsWidget;
            newsFilterMenu.IsVisible = newsWidget != null;
            if (newsWidget != null)
            {
                newsAllItem.IsChecked = newsWidget.Filter == NewsFilterType.All;
                newsJavaItem.IsChecked = newsWidget.Filter == NewsFilterType.Java;
                newsBedrockItem.IsChecked = newsWidget.Filter == NewsFilterType.Bedrock;
            }

            sizeMenu.Items.Clear();
            var definition = WidgetRegistry.Get(_contextMenuWidget.Layout.Kind);
            if (definition == null)
                return;

            foreach (var size in definition.SupportedSizes)
            {
                var item = new MenuItem
                {
                    Header = size.ToString(),
                    IsChecked = _contextMenuWidget.Layout.Size == size,
                    Classes = { "hide-icon" }
                };
                item.Click += (_, _) => SetWidgetSize(_contextMenuWidget, size);
                sizeMenu.Items.Add(item);
            }
        };

        _emptyContextMenu = new ContextMenu();
        var addItem = new MenuItem
        {
            Header = CommonLanguageManager.Instance.widgets_addWidget.CurrentValue(), Icon = IconResources.CreateIcon("\ue645", 16)
        };
        addItem.Click += (_, _) => AddWidgetCallOn?.Invoke(this, EventArgs.Empty);
        _emptyContextMenu.Items.Add(addItem);
    }

    private void SetBackgroundOverride(WidgetHost? host, bool? value)
    {
        if (host == null)
            return;

        host.Layout.ShowBackground = value;
        host.ApplyBackground();
        SaveLayout();
    }

    private void SetNewsFilter(NewsFilterType filter)
    {
        if (_contextMenuWidget?.WidgetContent is NewsWidget news)
        {
            news.SetFilter(filter);
            SaveLayout();
        }
    }

    private void OnDeleteWidgetClick(object? sender, EventArgs e)
    {
        if (_contextMenuWidget == null || _canvas == null)
            return;

        UnhookWidget(_contextMenuWidget);
        ClearWidgetOccupancy(_contextMenuWidget);
        _canvas.Children.Remove(_contextMenuWidget);
        _allWidgets.Remove(_contextMenuWidget);
        _contextMenuWidget = null;
        UpdateCanvasSize();
        SaveLayout();
    }

    private async Task ChangeImageAsync()
    {
        if (_contextMenuWidget?.WidgetContent is not ImageViewWidget img)
            return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = CommonLanguageManager.Instance.widgets_selectImage.CurrentValue(),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(CommonLanguageManager.Instance.widgets_image.CurrentValue())
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp", "*.ico"]
                }
            ]
        });

        if (files.Count == 0)
            return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
            return;

        img.UpdateImage(path);
        SaveLayout();
    }

    private void LoadLayoutFromConfig()
    {
        if (_canvas == null)
            return;

        ClearAllWidgets();
        foreach (var data in Data.ConfigEntry.WidgetLayout ?? [])
        {
            if (WidgetRegistry.Get(data.Kind) == null)
                continue;

            var host = CreateHost(data.Kind);
            if (host == null)
                continue;

            host.Layout = data;
            host.SetSize(data.Size);
            _canvas.Children.Add(host);
            _allWidgets.Add(host);
            HookWidget(host);
            host.ApplyBackground();
            PlaceWidgetAtGrid(host, new Point(data.GridX, data.GridY));
        }

        UpdateCanvasSize();
    }

    public WidgetHost? AddWidget(WidgetKind kind)
    {
        return AddWidget(kind, null);
    }

    public WidgetHost? AddWidget(WidgetKind kind, WidgetLayoutData? template)
    {
        if (_canvas == null)
            return null;

        var definition = WidgetRegistry.Get(kind);
        if (definition == null)
            return null;

        var host = CreateHost(kind);
        if (host == null)
            return null;

        if (template != null) host.Layout.Data = template.Data;

        host.Layout.Size = definition.DefaultSize;
        host.SetSize(definition.DefaultSize);

        var startPos = FindNearestFreeGridPosition(new Point(0, 0), host);
        PlaceWidgetAtGrid(host, startPos);
        _canvas.Children.Add(host);
        _allWidgets.Add(host);
        HookWidget(host);
        host.ApplyBackground();

        UpdateCanvasSize();
        SaveLayout();
        return host;
    }

    private WidgetHost? CreateHost(WidgetKind kind)
    {
        return new WidgetHost
        {
            Layout = new WidgetLayoutData { Kind = kind }
        };
    }

    private void HookWidget(WidgetHost widget)
    {
        widget.Resized += OnWidgetResized;
        widget.RightButtonPressed += OnWidgetRightButtonDown;
    }

    private void UnhookWidget(WidgetHost widget)
    {
        widget.Resized -= OnWidgetResized;
        widget.RightButtonPressed -= OnWidgetRightButtonDown;
    }

    private void OnWidgetRightButtonDown(object? sender, PointerPressedEventArgs e)
    {
        if (sender is WidgetHost widget)
        {
            _contextMenuWidget = widget;
            _widgetContextMenu?.Open(widget);
            e.Handled = true;
        }
    }

    private void OnWidgetResized(WidgetHost widget)
    {
        if (widget == null || _canvas == null)
            return;

        ClearWidgetOccupancy(widget);
        var currentPos = new Point(Canvas.GetLeft(widget), Canvas.GetTop(widget));
        var freeGridPos = FindNearestFreeGridPosition(currentPos, widget);
        PlaceWidgetAtGrid(widget, freeGridPos);
        UpdateCanvasSize();
        SaveLayout();
    }

    private void OnWorkspacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var source = e.Source as Visual;
        if (source == null)
            return;


        if (source.FindAncestorOfType<Button>() != null)
            return;
        if (source.FindAncestorOfType<WidgetHost>()?.IsResizeHandleArea(source) == true)
            return;

        var widget = source.FindAncestorOfType<WidgetHost>();
        if (widget == null)
            return;

        _pendingDragHost = widget;
        _pendingStartPoint = e.GetPosition(_canvas);
        _pendingInitialPosition = new Point(Canvas.GetLeft(widget), Canvas.GetTop(widget));
        e.Pointer.Capture(this);
    }

    private void OnWorkspacePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingWidget != null)
        {
            if (_canvas == null)
                return;

            var currentMousePos = e.GetPosition(_canvas);
            var deltaX = currentMousePos.X - _dragStartPoint.X;
            var deltaY = currentMousePos.Y - _dragStartPoint.Y;

            var newX = _widgetInitialPosition.X + deltaX;
            var newY = _widgetInitialPosition.Y + deltaY;

            Canvas.SetLeft(_draggingWidget, newX);
            Canvas.SetTop(_draggingWidget, newY);

            UpdateGhostPositionByCenter(newX, newY);
            return;
        }

        if (_pendingDragHost == null)
            return;

        var current = e.GetPosition(_canvas);
        var distance = Math.Sqrt(
            Math.Pow(current.X - _pendingStartPoint.X, 2) +
            Math.Pow(current.Y - _pendingStartPoint.Y, 2));
        if (distance < DragThreshold)
            return;

        BeginDrag(_pendingDragHost);
    }

    private void BeginDrag(WidgetHost widget)
    {
        _pendingDragHost = null;
        _draggingWidget = widget;
        _dragStartPoint = _pendingStartPoint;
        _widgetInitialPosition = _pendingInitialPosition;

        ClearWidgetOccupancy(widget);
        CreateGhostPlaceholder(widget);

        _canvas?.Children.Remove(widget);
        _canvas?.Children.Add(widget);
    }

    private void OnWorkspacePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggingWidget != null)
        {
            EndDrag();
            return;
        }


        if (_pendingDragHost is { } clickedHost)
        {
            _pendingDragHost = null;
            clickedHost.WidgetContent?.PerformClick();
        }
    }

    private void OnWorkspacePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _pendingDragHost = null;
        if (_draggingWidget != null)
            EndDrag();
    }

    private void EndDrag()
    {
        if (_draggingWidget == null || _canvas == null)
            return;

        var widget = _draggingWidget;
        _draggingWidget = null;
        RemoveGhostPlaceholder();

        var currentPos = new Point(Canvas.GetLeft(widget), Canvas.GetTop(widget));
        var freeGridPos = FindNearestFreeGridPosition(currentPos, widget);
        PlaceWidgetAtGrid(widget, freeGridPos);
        UpdateCanvasSize();
        SaveLayout();
    }

    private static int GetWidgetCols(WidgetHost widget)
    {
        return Math.Max(1, widget.Layout.Columns);
    }

    private static int GetWidgetRows(WidgetHost widget)
    {
        return Math.Max(1, widget.Layout.Rows);
    }

    private void CreateGhostPlaceholder(WidgetHost widget)
    {
        if (_canvas == null)
            return;

        var cols = GetWidgetCols(widget);
        var rows = GetWidgetRows(widget);

        _ghostPlaceholder = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1890ff"), 0.12),
            BorderBrush = new SolidColorBrush(Color.Parse("#1890ff"), 0.35),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Width = cols * WidgetGeometry.Pitch - WidgetGeometry.Spacing,
            Height = rows * WidgetGeometry.Pitch - WidgetGeometry.Spacing,
            IsVisible = false
        };
        _canvas.Children.Add(_ghostPlaceholder);
    }

    private void UpdateGhostPositionByCenter(double pixelLeft, double pixelTop)
    {
        if (_ghostPlaceholder == null || _draggingWidget == null)
            return;

        var widgetCols = GetWidgetCols(_draggingWidget);
        var widgetRows = GetWidgetRows(_draggingWidget);

        var widgetWidth = widgetCols * WidgetGeometry.Pitch - WidgetGeometry.Spacing;
        var widgetHeight = widgetRows * WidgetGeometry.Pitch - WidgetGeometry.Spacing;

        var centerX = pixelLeft + widgetWidth / 2;
        var centerY = pixelTop + widgetHeight / 2;

        var bestPos = FindNearestFreeGridPositionByCenter(centerX, centerY, widgetCols, widgetRows);
        if (bestPos != null)
        {
            _ghostPlaceholder.IsVisible = true;
            Canvas.SetLeft(_ghostPlaceholder, bestPos.Value.X * WidgetGeometry.Pitch);
            Canvas.SetTop(_ghostPlaceholder, bestPos.Value.Y * WidgetGeometry.Pitch);
        }
        else
        {
            _ghostPlaceholder.IsVisible = false;
        }
    }

    private void RemoveGhostPlaceholder()
    {
        if (_ghostPlaceholder != null && _canvas != null)
        {
            _canvas.Children.Remove(_ghostPlaceholder);
            _ghostPlaceholder = null;
        }
    }

    private Point? FindNearestFreeGridPositionByCenter(double centerX, double centerY, int widgetCols, int widgetRows)
    {
        var startCol = (int)Math.Floor(centerX / WidgetGeometry.Pitch);
        var startRow = (int)Math.Floor(centerY / WidgetGeometry.Pitch);

        var searchRadius = Math.Max(widgetCols, widgetRows) + 6;

        List<(Point pos, double distance)> candidates = [];
        for (var r = Math.Max(0, startRow - searchRadius); r <= startRow + searchRadius; r++)
        for (var c = Math.Max(0, startCol - searchRadius); c <= startCol + searchRadius; c++)
        {
            if (!IsAreaFree(c, r, widgetCols, widgetRows))
                continue;

            var gridCenterX = c * WidgetGeometry.Pitch +
                              (widgetCols * WidgetGeometry.Pitch - WidgetGeometry.Spacing) / 2;
            var gridCenterY = r * WidgetGeometry.Pitch +
                              (widgetRows * WidgetGeometry.Pitch - WidgetGeometry.Spacing) / 2;

            var dist = Math.Pow(centerX - gridCenterX, 2) + Math.Pow(centerY - gridCenterY, 2);
            candidates.Add((new Point(c, r), dist));
        }

        if (candidates.Count == 0)
            return null;

        candidates.Sort((a, b) => a.distance.CompareTo(b.distance));
        return candidates[0].pos;
    }

    private Point FindNearestFreeGridPosition(Point targetPixelPos, WidgetHost widget)
    {
        var widgetCols = GetWidgetCols(widget);
        var widgetRows = GetWidgetRows(widget);

        var widgetWidth = widgetCols * WidgetGeometry.Pitch - WidgetGeometry.Spacing;
        var widgetHeight = widgetRows * WidgetGeometry.Pitch - WidgetGeometry.Spacing;

        var centerX = targetPixelPos.X + widgetWidth / 2;
        var centerY = targetPixelPos.Y + widgetHeight / 2;

        var bestPos = FindNearestFreeGridPositionByCenter(centerX, centerY, widgetCols, widgetRows);
        return bestPos ?? new Point(0, 0);
    }

    private bool IsAreaFree(int col, int row, int cols, int rows)
    {
        for (var c = 0; c < cols; c++)
        for (var r = 0; r < rows; r++)
            if (_occupiedGridCells.ContainsKey(new Point(col + c, row + r)))
                return false;

        return true;
    }

    private void PlaceWidgetAtGrid(WidgetHost widget, Point gridPos)
    {
        var pixelX = gridPos.X * WidgetGeometry.Pitch;
        var pixelY = gridPos.Y * WidgetGeometry.Pitch;

        Canvas.SetLeft(widget, pixelX);
        Canvas.SetTop(widget, pixelY);
        widget.Layout.GridX = (int)gridPos.X;
        widget.Layout.GridY = (int)gridPos.Y;

        RegisterWidgetOccupancy(widget, gridPos);
    }

    private void RegisterWidgetOccupancy(WidgetHost widget, Point gridPos)
    {
        var cols = GetWidgetCols(widget);
        var rows = GetWidgetRows(widget);

        for (var c = 0; c < cols; c++)
        for (var r = 0; r < rows; r++)
            _occupiedGridCells[new Point(gridPos.X + c, gridPos.Y + r)] = widget;
    }

    private void ClearWidgetOccupancy(WidgetHost widget)
    {
        var keysToRemove = _occupiedGridCells.Where(kvp => kvp.Value == widget).Select(kvp => kvp.Key).ToList();
        foreach (var key in keysToRemove)
            _occupiedGridCells.Remove(key);
    }

    private void ClearAllWidgets()
    {
        if (_canvas == null)
            return;

        foreach (var widget in _allWidgets)
        {
            UnhookWidget(widget);
            _canvas.Children.Remove(widget);
        }

        _allWidgets.Clear();
        _occupiedGridCells.Clear();
    }

    private void UpdateCanvasSize()
    {
        if (_canvas == null)
            return;

        _canvas.Width = Math.Max(0, Bounds.Width - 2 * WidgetGeometry.Spacing);
        _canvas.Height = Math.Max(0, Bounds.Height - 2 * WidgetGeometry.Spacing);
    }

    private void SaveLayout()
    {
        Data.ConfigEntry.WidgetLayout = _allWidgets.Select(widget => widget.Layout).ToList();
    }

    private void SetWidgetSize(WidgetHost widget, WidgetCellSize size)
    {
        widget.SetSize(size);
        OnWidgetResized(widget);
    }
}
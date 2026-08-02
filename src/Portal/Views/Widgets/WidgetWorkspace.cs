using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Portal.Classes.Entries;
using Portal.Const;
using Portal.Module.Widgets;
using Portal.ViewModels;

namespace Portal.Views.Widgets;

public class WidgetWorkspace : UserControl
{
    private const double DragThreshold = 6;

    private Canvas? _canvas;

    private WidgetHost? _pendingDragHost;
    private Point _pendingStartPoint;
    private Point _pendingInitialPosition;

    private WidgetHost? _draggingWidget;
    private Point _dragStartPoint;
    private Point _widgetInitialPosition;

    private Border? _ghostPlaceholder;

    private readonly Dictionary<Point, WidgetHost> _occupiedGridCells = [];
    private readonly List<WidgetHost> _allWidgets = [];

    private ContextMenu? _widgetContextMenu;
    private ContextMenu? _emptyContextMenu;
    private WidgetHost? _contextMenuWidget;

    public event EventHandler? AddWidgetCallOn;

    public WidgetWorkspace()
    {
        _canvas = new Canvas
        {
            // Margin = new Thickness(WidgetGeometry.Spacing),
            Background = new SolidColorBrush(Colors.Transparent)
        };
        Content = _canvas;
        ClipToBounds = true;

        _canvas.PointerPressed += OnCanvasPointerPressed;
        AddHandler(InputElement.PointerPressedEvent, OnWorkspacePointerPressed, RoutingStrategies.Bubble,
            handledEventsToo: true);
        PointerMoved += OnWorkspacePointerMoved;
        PointerReleased += OnWorkspacePointerReleased;
        PointerCaptureLost += OnWorkspacePointerCaptureLost;
        SizeChanged += OnWorkspaceSizeChanged;
        InitializeContextMenus();
        Loaded += (_, _) => LoadLayoutFromConfig();
        UpdateCanvasSize();
    }

    private void OnWorkspaceSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateCanvasSize();

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
            Header = "删除组件", Icon = new PathIcon()
            {
                Data = StreamGeometry.Parse(
                    "F1 M640,640z M0,0z M232.7,69.9C237.1,56.8,249.3,48,263.1,48L377,48C390.8,48,403,56.8,407.4,69.9L416,96 512,96C529.7,96 544,110.3 544,128 544,145.7 529.7,160 512,160L128,160C110.3,160 96,145.7 96,128 96,110.3 110.3,96 128,96L224,96 232.7,69.9z M128,208L512,208 512,512C512,547.3,483.3,576,448,576L192,576C156.7,576,128,547.3,128,512L128,208z M216,272C202.7,272,192,282.7,192,296L192,488C192,501.3 202.7,512 216,512 229.3,512 240,501.3 240,488L240,296C240,282.7,229.3,272,216,272z M320,272C306.7,272,296,282.7,296,296L296,488C296,501.3 306.7,512 320,512 333.3,512 344,501.3 344,488L344,296C344,282.7,333.3,272,320,272z M424,272C410.7,272,400,282.7,400,296L400,488C400,501.3 410.7,512 424,512 437.3,512 448,501.3 448,488L448,296C448,282.7,437.3,272,424,272z"),
                Width = 16, Height = 16
            }
        };
        deleteItem.Click += OnDeleteWidgetClick;
        _widgetContextMenu.Items.Add(deleteItem);

        var backgroundMenu = new MenuItem { Header = "背景", Icon = new PathIcon()
        {
            Data = StreamGeometry.Parse(
                "F1 M640,640z M0,0z M512,128C547.3,128,576,156.7,576,192L576,341.5C576,358.5,569.3,374.8,557.3,386.8L450.7,493.3C438.7,505.3,422.4,512,405.4,512L128,512C92.7,512,64,483.3,64,448L64,192C64,156.7,92.7,128,128,128L512,128z M517.5,336L424,336C410.7,336,400,346.7,400,360L400,453.5 517.5,336z M160,256C177.7,256 192,241.7 192,224 192,206.3 177.7,192 160,192 142.3,192 128,206.3 128,224 128,241.7 142.3,256 160,256z"),
            Width = 16, Height = 16
        } };
        var followItem = new MenuItem { Header = "跟随全局", Classes = { "hide-icon" } };
        followItem.Click += (_, _) => SetBackgroundOverride(_contextMenuWidget, null);
        var showItem = new MenuItem { Header = "始终显示", Classes = { "hide-icon" } };
        showItem.Click += (_, _) => SetBackgroundOverride(_contextMenuWidget, true);
        var hideItem = new MenuItem { Header = "始终隐藏", Classes = { "hide-icon" } };
        hideItem.Click += (_, _) => SetBackgroundOverride(_contextMenuWidget, false);
        backgroundMenu.Items.Add(followItem);
        backgroundMenu.Items.Add(showItem);
        backgroundMenu.Items.Add(hideItem);
        _widgetContextMenu.Items.Add(backgroundMenu);

        var memoryModeItem = new MenuItem
        {
            Header = "切换显示模式",
            Icon = new PathIcon()
            {
                Data = StreamGeometry.Parse(
                    "F1 M640,640z M0,0z M320,0C441.9,0 547.7,67.9 604.3,167.6L551.7,202.2C508.6,127.6 427.5,80 336,80L320,80C171.9,80 56,195.9 56,344L56,376 152,376 152,344C152,249.1 225.1,176 320,176L336,176C427.5,176 508.6,223.6 551.7,298.2L604.3,263.6C547.7,163.9 441.9,96 320,96L320,0z M320,512C414.9,512 488,438.9 488,344L488,312 584,312 584,344C584,492.1 468.1,608 320,608L320,512z M320,432A80,80 0 1,0 160,432A80,80 0 1,0 320,432z"),
                Width = 16, Height = 16
            },
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

        // 新闻过滤：全部 / 仅 Java 版 / 仅基岩版
        var newsFilterMenu = new MenuItem
        {
            Header = "新闻过滤",
            Icon = new PathIcon
            {
                Data = StreamGeometry.Parse(
                    "F1 M640,640z M0,0z M128,96C128,78 142,64 160,64L480,64C498,64 512,78 512,96L512,544C512,562 498,576 480,576L160,576C142,576 128,562 128,544L128,96z M192,160L192,192H448V160H192z M192,256V288H448V256H192z M192,352V384H352V352H192z"),
                Width = 16, Height = 16
            },
            IsVisible = false
        };
        var newsAllItem = new MenuItem { Header = "全部", Classes = { "hide-icon" } };
        newsAllItem.Click += (_, _) => SetNewsFilter(NewsFilterType.All);
        var newsJavaItem = new MenuItem { Header = "仅 Java 版", Classes = { "hide-icon" } };
        newsJavaItem.Click += (_, _) => SetNewsFilter(NewsFilterType.Java);
        var newsBedrockItem = new MenuItem { Header = "仅基岩版", Classes = { "hide-icon" } };
        newsBedrockItem.Click += (_, _) => SetNewsFilter(NewsFilterType.Bedrock);
        newsFilterMenu.Items.Add(newsAllItem);
        newsFilterMenu.Items.Add(newsJavaItem);
        newsFilterMenu.Items.Add(newsBedrockItem);
        _widgetContextMenu.Items.Add(newsFilterMenu);

        var imageChangeItem = new MenuItem
        {
            Header = "更换图片",
            Icon = new PathIcon()
            {
                Data = StreamGeometry.Parse(
                    "F1 M640,640z M0,0z M448,128C448,93,419,64,384,64L256,64C221,64,192,93,192,128L192,256L128,256C93,256,64,285,64,320L64,448C64,483,93,512,128,512L256,512C291,512,320,483,320,448L320,384L384,384C419,384,448,355,448,320L448,128z M320,448C320,466,306,480,288,480L160,480C142,480,128,466,128,448L128,320C128,302,142,288,160,288L192,288L192,320C192,355,221,384,256,384L320,384L320,448z M384,320C384,338,370,352,352,352L224,352C206,352,192,338,192,320L192,192C192,174,206,160,224,160L352,160C370,160,384,174,384,192L384,320z"),
                Width = 16, Height = 16
            },
            IsVisible = false
        };
        imageChangeItem.Click += (_, _) => _ = ChangeImageAsync();
        _widgetContextMenu.Items.Add(imageChangeItem);

        var imageStretchItem = new MenuItem
        {
            Header = "切换填充方式",
            Icon = new PathIcon()
            {
                Data = StreamGeometry.Parse(
                    "F1 M640,640z M0,0z M128,96C128,78,142,64,160,64L480,64C498,64,512,78,512,96L512,416C512,434,498,448,480,448L160,448C142,448,128,434,128,416L128,96z M64,480C64,462,78,448,96,448L544,448C562,448,576,462,576,480L576,512C576,530,562,544,544,544L96,544C78,544,64,530,64,512L64,480z"),
                Width = 16, Height = 16
            },
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

        var sizeMenu = new MenuItem { Header = "切换尺寸", Icon = new PathIcon()
        {
            Data = StreamGeometry.Parse(
                "F1 M640,640z M0,0z M241.1,580.2C222.4,598.9,192,598.9,173.2,580.2L60.1,467.1C41.4,448.4,41.4,418,60.1,399.2L77.1,382.2 150.6,455.7C160,465.1 175.2,465.1 184.5,455.7 193.8,446.3 193.9,431.1 184.5,421.8L111,348.3 144.9,314.4 195.8,365.3C205.2,374.7 220.4,374.7 229.7,365.3 239,355.9 239.1,340.7 229.7,331.4L178.8,280.5 212.7,246.6 286.2,320.1C295.6,329.5 310.8,329.5 320.1,320.1 329.4,310.7 329.5,295.5 320.1,286.2L246.6,212.7 280.5,178.8 331.4,229.7C340.8,239.1 356,239.1 365.3,229.7 374.6,220.3 374.7,205.1 365.3,195.8L314.4,144.9 348.3,111 421.8,184.5C431.2,193.9 446.4,193.9 455.7,184.5 465,175.1 465.1,159.9 455.7,150.6L382.2,77.1 399.2,60.1C417.9,41.4,448.3,41.4,467.1,60.1L580.5,172.9C599.2,191.6,599.2,222,580.5,240.8L241.1,580.2z"),
            Width = 16, Height = 16
        } };
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

            NewsWidget? newsWidget = _contextMenuWidget.WidgetContent as NewsWidget;
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
            Header = "添加组件", Icon = new PathIcon()
            {
                Data = StreamGeometry.Parse(
                    "F1 M640,640z M0,0z M352,128C352,110.3 337.7,96 320,96 302.3,96 288,110.3 288,128L288,288 128,288C110.3,288 96,302.3 96,320 96,337.7 110.3,352 128,352L288,352 288,512C288,529.7 302.3,544 320,544 337.7,544 352,529.7 352,512L352,352 512,352C529.7,352 544,337.7 544,320 544,302.3 529.7,288 512,288L352,288 352,128z"),
                Width = 16, Height = 16
            }
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

    /// <summary>为当前图片小组件弹出文件选择器更换图片。</summary>
    private async Task ChangeImageAsync()
    {
        if (_contextMenuWidget?.WidgetContent is not ImageViewWidget img)
            return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择图片",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("图片")
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

    /// <summary>添加组件，自动放到最近的空闲位置。</summary>
    public WidgetHost? AddWidget(WidgetKind kind)
    {
        return AddWidget(kind, null);
    }

    /// <summary>添加组件并应用模板布局数据中的配置字段（实例、世界、服务器等），自动放到最近的空闲位置。</summary>
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

        if (template != null)
        {
            host.Layout.Data = template.Data;
        }

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

        // 按钮与缩放手柄自行处理交互，不参与拖拽
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
            double deltaX = currentMousePos.X - _dragStartPoint.X;
            double deltaY = currentMousePos.Y - _dragStartPoint.Y;

            double newX = _widgetInitialPosition.X + deltaX;
            double newY = _widgetInitialPosition.Y + deltaY;

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

        // 未发生拖动时视为点击，交给组件内容自行处理（例如打开详情页）。
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

    private static int GetWidgetCols(WidgetHost widget) => Math.Max(1, widget.Layout.Columns);

    private static int GetWidgetRows(WidgetHost widget) => Math.Max(1, widget.Layout.Rows);

    private void CreateGhostPlaceholder(WidgetHost widget)
    {
        if (_canvas == null)
            return;

        int cols = GetWidgetCols(widget);
        int rows = GetWidgetRows(widget);

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

        int widgetCols = GetWidgetCols(_draggingWidget);
        int widgetRows = GetWidgetRows(_draggingWidget);

        double widgetWidth = widgetCols * WidgetGeometry.Pitch - WidgetGeometry.Spacing;
        double widgetHeight = widgetRows * WidgetGeometry.Pitch - WidgetGeometry.Spacing;

        double centerX = pixelLeft + widgetWidth / 2;
        double centerY = pixelTop + widgetHeight / 2;

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
        int startCol = (int)Math.Floor(centerX / WidgetGeometry.Pitch);
        int startRow = (int)Math.Floor(centerY / WidgetGeometry.Pitch);

        int searchRadius = Math.Max(widgetCols, widgetRows) + 6;

        List<(Point pos, double distance)> candidates = [];
        for (int r = Math.Max(0, startRow - searchRadius); r <= startRow + searchRadius; r++)
        {
            for (int c = Math.Max(0, startCol - searchRadius); c <= startCol + searchRadius; c++)
            {
                if (!IsAreaFree(c, r, widgetCols, widgetRows))
                    continue;

                double gridCenterX = c * WidgetGeometry.Pitch +
                                     (widgetCols * WidgetGeometry.Pitch - WidgetGeometry.Spacing) / 2;
                double gridCenterY = r * WidgetGeometry.Pitch +
                                     (widgetRows * WidgetGeometry.Pitch - WidgetGeometry.Spacing) / 2;

                double dist = Math.Pow(centerX - gridCenterX, 2) + Math.Pow(centerY - gridCenterY, 2);
                candidates.Add((new Point(c, r), dist));
            }
        }

        if (candidates.Count == 0)
            return null;

        candidates.Sort((a, b) => a.distance.CompareTo(b.distance));
        return candidates[0].pos;
    }

    private Point FindNearestFreeGridPosition(Point targetPixelPos, WidgetHost widget)
    {
        int widgetCols = GetWidgetCols(widget);
        int widgetRows = GetWidgetRows(widget);

        double widgetWidth = widgetCols * WidgetGeometry.Pitch - WidgetGeometry.Spacing;
        double widgetHeight = widgetRows * WidgetGeometry.Pitch - WidgetGeometry.Spacing;

        double centerX = targetPixelPos.X + widgetWidth / 2;
        double centerY = targetPixelPos.Y + widgetHeight / 2;

        var bestPos = FindNearestFreeGridPositionByCenter(centerX, centerY, widgetCols, widgetRows);
        return bestPos ?? new Point(0, 0);
    }

    private bool IsAreaFree(int col, int row, int cols, int rows)
    {
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                if (_occupiedGridCells.ContainsKey(new Point(col + c, row + r)))
                    return false;
            }
        }

        return true;
    }

    private void PlaceWidgetAtGrid(WidgetHost widget, Point gridPos)
    {
        double pixelX = gridPos.X * WidgetGeometry.Pitch;
        double pixelY = gridPos.Y * WidgetGeometry.Pitch;

        Canvas.SetLeft(widget, pixelX);
        Canvas.SetTop(widget, pixelY);
        widget.Layout.GridX = (int)gridPos.X;
        widget.Layout.GridY = (int)gridPos.Y;

        RegisterWidgetOccupancy(widget, gridPos);
    }

    private void RegisterWidgetOccupancy(WidgetHost widget, Point gridPos)
    {
        int cols = GetWidgetCols(widget);
        int rows = GetWidgetRows(widget);

        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                _occupiedGridCells[new Point(gridPos.X + c, gridPos.Y + r)] = widget;
            }
        }
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
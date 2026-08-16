using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Portal.Classes.Config;
using Portal.Core.Classes;
using Portal.Core.Classes.Entries;
using Portal.Core.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Module.Initialize;
using Portal.Core.Operations.OpenFile;
using Portal.Module;
using Portal.Module.DefaultPage;
using Portal.Views.Components;
using Portal.Views.Pages;
using Portal.Views.Pages.DownloadPages;
using SkiaSharp;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Helper;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Common.Helpers;
using TioUi.Controls;
using AutoCompleteBox = Avalonia.Controls.AutoCompleteBox;
#if DEBUG
using HotAvalonia;
#endif

namespace Portal.Views;

public partial class TabWindow : TioTabWindowBase
{
    private Image? _backgroundImageLayer;
    private Border? _backgroundMaskLayer;
    private string? _cachedBackgroundPath;
    private Bitmap? _cachedOriginalBackground;
    private Debouncer _hideDropTipDebouncer;


    private bool _hideDropTipScheduled;
    private bool _isConfigEntrySubscribed;
    private string? _lastDragMessage;
    private IntPtr _macOsWindowHandle;

    public TabWindow()
    {
        Build();
    }

    public TabWindow(bool isMainWindow)
    {
        IsMainWindow = isMainWindow;
        Build();
    }

    public bool IsTabMaskVisible
    {
        get;
        set => SetField(ref field, value);
    }

    public bool IsUiLoading
    {
        get;
        set => SetField(ref field, value);
    }

    public override bool OnClose()
    {
        if (AllWindows.Count == 1)
        {
            ConfigSaver.FlushConfig();
            Environment.Exit(0);
            return true;
        }

        return false;
    }

    private void Build()
    {
        _hideDropTipDebouncer = new Debouncer(OnHideDropTipDebounce, 300);
        InitializeComponent();


        if (Data.ConfigEntry.HasTabWindowSize)
        {
            Width = Math.Max(Data.ConfigEntry.TabWindowWidth, MinWidth);
            Height = Math.Max(Data.ConfigEntry.TabWindowHeight, MinHeight);
        }

        Notification = new TioNotificationManager(this);
        Toast = new TioToastManager(this);
        Window = this;
        DataContext = this;
        Events();
        Keys();
        AttachDropDrag();
        CreateNewTabFunc = () =>
        {
            ITioTabPage page = Data.ConfigEntry.NewTabContent switch
            {
                NewTabContent.NewTabPage => new NewTabPage(),
                NewTabContent.StartPage => new StartPage(),
                NewTabContent.Widget => new WidgetsPage(),
                _ => new NewTabPage()
            };
            var tab = new TabEntry(this, page)
            {
                IconHeight = 17,
                IconWidth = 17,
                IconMargin = Data.ConfigEntry.NewTabContent switch
                {
                    _ => new Thickness(0, 0, 4, 0)
                }
            };

            AddTab(tab);
            SelectTab(tab);
            NavScrollViewer.Offset = new Vector(double.PositiveInfinity, 0);
        };
        if (IsMainWindow)
        {
            IsUiLoading = true;
            var pageType = DefaultPageRegistry.Pages
                .FirstOrDefault(item => item.PageType.AssemblyQualifiedName == Data.ConfigEntry.DefaultPage)
                ?.PageType;
            var page = pageType != null && Activator.CreateInstance(pageType) is ITioTabPage tabPage
                ? tabPage
                : new NewTabPage();
            var tab = new TabEntry(this, page);
            AddTab(tab);
            SelectTab(tab);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            TabSelectionList.EnableTabDragDrop(this);
        else
            TabSelectionList.PointerPressed += (_, e) =>
            {
                if (!e.Properties.IsLeftButtonPressed) return;
                BeginMoveDrag(e);
            };

        Loaded += (_, _) => ApplyBackground();
    }

#if DEBUG
    [AvaloniaHotReload]
#endif
    public void Hot()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) TabSelectionList.EnableTabDragDrop(this);
    }

    private void Events()
    {
        Closed += TabWindow_OnClosed;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var platform = TryGetPlatformHandle();
            if (platform is null) return;
            var nsWindow = platform.Handle;
            if (nsWindow == IntPtr.Zero) return;
            _macOsWindowHandle = nsWindow;
            Loaded += (_, _) => { MacOsWindowHandler(nsWindow); };
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(WindowState)) return;
                MacOsWindowHandler(nsWindow);
            };
            SizeChanged += (_, _) => { MacOsWindowHandler(nsWindow); };
            Data.ConfigEntry.PropertyChanged += ConfigEntry_OnPropertyChanged;
            _isConfigEntrySubscribed = true;
            TitleBarThings.SizeChanged += (_, _) =>
            {
                NavScrollViewer.Margin =
                    new Thickness(TitleBarLogo.Bounds.Width + 86, -44, TitleBarThings.Bounds.Width + 15 + 30, 0);
            };
        }
        else
        {
            TitleBarThings.SizeChanged += (_, _) =>
            {
                NavScrollViewer.Margin = new Thickness(TitleBarLogo.Bounds.Width + 3, -44,
                    90 + 30 + TitleBarThings.Bounds.Width, 0);
            };
        }

        NavScrollViewer.ScrollChanged += (_, _) => { IsTabMaskVisible = NavScrollViewer.Offset.X > 0; };
        SizeChanged += TabWindow_OnSizeChanged;
        Resized += TabWindow_OnResized;
        return;

        void MacOsWindowHandler(IntPtr nsWindow)
        {
            try
            {
                TioUi.Common.Helpers.MacOsWindowHandler.RefreshTitleBarButtonPosition(nsWindow, 16, -3,
                    23);
            }
            catch (Exception exception)
            {
                Logger.Error(exception);
            }
        }
    }

    private void ConfigEntry_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Data.ConfigEntry.Theme) || _macOsWindowHandle == IntPtr.Zero)
            return;

        try
        {
            MacOsWindowHandler.RefreshTitleBarButtonPosition(_macOsWindowHandle, 16, -3,
                23);
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
        }
    }

    private void TabWindow_OnClosed(object? sender, EventArgs e)
    {
        if (_isConfigEntrySubscribed)
        {
            Data.ConfigEntry.PropertyChanged -= ConfigEntry_OnPropertyChanged;
            _isConfigEntrySubscribed = false;
        }

        _macOsWindowHandle = IntPtr.Zero;


        TabSelectionList.DisableTabDragDrop();

        RemoveHandler(DragDrop.DragLeaveEvent, OnLeaveHandler);
        RemoveHandler(DragDrop.DragOverEvent, OnDragHandler);
        RemoveHandler(DragDrop.DropEvent, OnDropHandler);

        SizeChanged -= TabWindow_OnSizeChanged;
        Resized -= TabWindow_OnResized;
        Closed -= TabWindow_OnClosed;

        ClearBackgroundLayers();
        ClearOriginalBackgroundCache();
        DataContext = null;
    }

    private void TabWindow_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (Data.ConfigEntry.BackgroundMode != BackgroundMode.Image || _cachedOriginalBackground == null)
            return;

        var pixelSize = _cachedOriginalBackground.PixelSize;
        var scale = RenderScaling;
        var width = (int)Math.Ceiling(e.NewSize.Width * scale);
        var height = (int)Math.Ceiling(e.NewSize.Height * scale);
        if (width > pixelSize.Width * 1.5 || height > pixelSize.Height * 1.5)
        {
            ClearOriginalBackgroundCache();
            ApplyBackground();
        }
    }

    private void TabWindow_OnResized(object? sender, WindowResizedEventArgs e)
    {
        if (WindowState == WindowState.Maximized || WindowState == WindowState.Minimized) return;
        if (e.Reason != WindowResizeReason.User) return;

        var size = e.ClientSize;
        if (size.Width <= 0 || size.Height <= 0) return;

        Data.ConfigEntry.TabWindowWidth = size.Width;
        Data.ConfigEntry.TabWindowHeight = size.Height;
        Data.ConfigEntry.HasTabWindowSize = true;
        ConfigSaver.SaveConfig();
    }

    public void OpenAggregatedSearchDialog()
    {
        if (FocusManager?.GetFocusedElement() is TextBox or AutoCompleteBox
            or TioUi.Controls.AutoCompleteBox)
            return;

        var options = new DialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            StyleClass = "undrag",
            CanResize = true,
            StartupLocation = WindowStartupLocation.CenterOwner,
            DialogWindowMinWidth = 770,
            DialogWindowMinHeight = 471,
            DialogWindowWidth = 770,
            DialogWindowHeight = 471,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        _ = Dialog.ShowCustomAsync<AggregatedSearchDialog, AggregatedSearchDialogViewModel, object>(
            new AggregatedSearchDialogViewModel(this), options: options, owner: this);
    }

    private void Keys()
    {
        RemoveDefaultWindowKeyBindings();
        ShortcutManager.Apply(this);
    }

    private void RemoveDefaultWindowKeyBindings()
    {
        var toRemove = KeyBindings
            .Where(binding => binding.Gesture is KeyGesture gesture && IsDefaultWindowGesture(gesture))
            .ToArray();
        foreach (var binding in toRemove)
            KeyBindings.Remove(binding);
    }

    private static bool IsDefaultWindowGesture(KeyGesture gesture)
    {
        return (gesture.Key == Key.T && gesture.KeyModifiers == KeyModifiers.Control) ||
               (gesture.Key == Key.W && gesture.KeyModifiers == KeyModifiers.Control) ||
               (gesture.Key == Key.W && gesture.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift));
    }

    public void OpenPage(ITioTabPage page)
    {
        var tab = new TabEntry(this, page);
        CreateTab(tab);
        SelectTab(tab);
    }

    public void OpenDebugPage()
    {
        OpenPage(new DebugPage());
    }

    public void OpenCreateInstanceDialog()
    {
        var options = new OverlayDialogOptions
        {
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            CanResize = false,
            IsCloseButtonVisible = false
        };
        _ = OverlayDialog.ShowCustomAsync<CreateInstanceDialog, CreateInstanceDialogViewModel, bool>(
            new CreateInstanceDialogViewModel(), this.TryGetHostId(), options);
    }

    public async void OpenAddMinecraftFolderDialog()
    {
        var options = new OverlayDialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            CanResize = false,
            VerticalAnchor = VerticalPosition.Top,
            VerticalOffset = 110
        };

        var result = await OverlayDialog
            .ShowCustomAsync<NewMinecraftFolder, NewMinecraftFolderViewModel, MinecraftFolderEntry>(
                new NewMinecraftFolderViewModel(Data.ConfigEntry.MinecraftFolders
                    .Select(folder => folder.FolderPath).ToList()), this.TryGetHostId(), options);

        if (result == null) return;
        Data.ConfigEntry.MinecraftFolders.Add(result);
    }

    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        CreateNewTabFunc();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            if (sender is not Border border) return;
            var tab = border.Tag as TabEntry;
            if (tab == null) return;
            var flyout = tab.BuildContextMenu();
            flyout.ShowAt(border);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsMiddleButtonPressed) return;
        var c = ((Border)sender).Tag as TabEntry;
        c?.Close();
    }

    private void InputElement_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;
        scrollViewer.Offset = new Vector(
            scrollViewer.Offset.X + e.Delta.Y * -20,
            scrollViewer.Offset.Y
        );
        e.Handled = true;
    }

    private void NM_NewTab(object? sender, EventArgs e)
    {
        CreateNewTabFunc();
    }

    private void NM_CloseTab(object? sender, EventArgs e)
    {
        SelectedTab.Close();
    }

    private void NM_CloseOtherTab(object? sender, EventArgs e)
    {
        SelectedTab.CloseOther();
    }

    private void NM_OpenInNewWindow(object? sender, EventArgs e)
    {
        SelectedTab.MoveTabToNewWindow();
    }

    private void AttachDropDrag()
    {
        DragDrop.SetAllowDrop(this, true);


        AddHandler(DragDrop.DragLeaveEvent, OnLeaveHandler);
        AddHandler(DragDrop.DragOverEvent, OnDragHandler);
        AddHandler(DragDrop.DropEvent, OnDropHandler);
    }

    private void OnDragHandler(object? sender, DragEventArgs e)
    {
        _hideDropTipScheduled = false;

        var msg = DragDropHandler.GetMsg(e);


        if (string.IsNullOrEmpty(msg) || msg == _lastDragMessage) return;
        _lastDragMessage = msg;
        BarComponent.DropMsg = msg;
    }

    private void OnLeaveHandler(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;


        _hideDropTipScheduled = true;
        _hideDropTipDebouncer.Invoke();
    }

    private void OnHideDropTipDebounce()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_hideDropTipScheduled) return;
            _hideDropTipScheduled = false;
            BarComponent.DropMsg = null;
            _lastDragMessage = null;
        });
    }

    private void OnDropHandler(object? sender, DragEventArgs e)
    {
        _hideDropTipScheduled = false;
        BarComponent.DropMsg = null;
        _lastDragMessage = null;
        DragDropHandler.ResetDragIdentification();
        DragDropHandler.Handle(e, this);
    }

    public static void ApplyBackgroundToAllWindows()
    {
        foreach (var windowBase in AllWindows)
            if (windowBase is TabWindow tabWin)
                tabWin.ApplyBackground();
    }

    public static void ApplyImageMaskToAllWindows()
    {
        foreach (var windowBase in AllWindows)
            if (windowBase is TabWindow tabWin)
                tabWin.ApplyImageMaskOverlay();
    }

    public void ApplyBackground()
    {
        var entry = Data.ConfigEntry;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (entry.EnableManagedWindowDecorationsOnWindows)
            {
                WindowDecorations = WindowDecorations.None;
                FrameBorderThickness = new Thickness(1);
                FrameBorderCornerRadius = new CornerRadius(10);
                FrameBorderBrush = entry.EnableManagedWindowBorderOnWindows
                    ? new SolidColorBrush(entry.CustomWindowBorderColor)
                    : new SolidColorBrush(Colors.Transparent);
                IsManagedResizerVisible = true;
            }
            else
            {
                WindowDecorations = WindowDecorations.Full;
                FrameBorderThickness = new Thickness(0);
                FrameBorderCornerRadius = new CornerRadius(0);
                IsManagedResizerVisible = false;
            }
        }

        switch (entry.BackgroundMode)
        {
            case BackgroundMode.Default:
                ClearOriginalBackgroundCache();
                ClearBackgroundLayers();
                ClearValue(BackgroundProperty);
                ClearValue(TransparencyBackgroundFallbackProperty);
                TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
                break;

            case BackgroundMode.Transparent:
                ClearOriginalBackgroundCache();
                ClearBackgroundLayers();
                Background = Brushes.Transparent;
                TransparencyBackgroundFallback = Brushes.Transparent;
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
                break;

            case BackgroundMode.Image:
                if (!string.IsNullOrEmpty(entry.BackgroundImagePath) && File.Exists(entry.BackgroundImagePath))
                {
                    try
                    {
                        var original = GetOrCreateOriginalBackground(entry.BackgroundImagePath);
                        EnsureBackgroundLayers();
                        _backgroundImageLayer!.Source = original;
                        UpdateImageBlurEffect(entry.ImageBlurRadius);
                        ClearValue(BackgroundProperty);
                        ClearValue(TransparencyBackgroundFallbackProperty);
                        TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
                    }
                    catch (Exception exception)
                    {
                        Logger.Error(exception);
                        ClearOriginalBackgroundCache();
                        ClearBackgroundLayers();
                    }
                }
                else
                {
                    ClearOriginalBackgroundCache();
                    ClearBackgroundLayers();
                    ClearValue(TransparencyBackgroundFallbackProperty);
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
                }

                break;

            case BackgroundMode.Color:
                ClearOriginalBackgroundCache();
                ClearBackgroundLayers();
                Background = Brushes.Transparent;
                if (RootBorder != null)
                    RootBorder.Background = new SolidColorBrush(entry.BackgroundSolidColor);
                TransparencyBackgroundFallback = Brushes.Transparent;
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
                break;

            case BackgroundMode.Acrylic:
                ClearOriginalBackgroundCache();
                ClearBackgroundLayers();
                var color = entry.BackgroundSolidColor;
                var alpha = (byte)(entry.AcrylicOpacity * 255);
                var acrylicBrush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
                Background = Brushes.Transparent;
                if (RootBorder != null)
                    RootBorder.Background = acrylicBrush;
                TransparencyBackgroundFallback = new SolidColorBrush(Color.FromArgb(255, color.R, color.G, color.B));
                TransparencyLevelHint = new[] { WindowTransparencyLevel.AcrylicBlur };
                break;


            case BackgroundMode.Mica:
                ClearOriginalBackgroundCache();
                ClearBackgroundLayers();
                var micaColor = entry.BackgroundSolidColor;
                var micaAlpha = (byte)(entry.MicaOpacity * 255);
                var micaBrush = new SolidColorBrush(Color.FromArgb(micaAlpha, micaColor.R, micaColor.G, micaColor.B));
                Background = Brushes.Transparent;
                if (RootBorder != null)
                    RootBorder.Background = micaBrush;
                TransparencyBackgroundFallback =
                    new SolidColorBrush(Color.FromArgb(255, micaColor.R, micaColor.G, micaColor.B));
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Mica };
                break;
        }

        ApplyImageMaskOverlay();
    }

    private void ApplyImageMaskOverlay()
    {
        if (_backgroundMaskLayer == null) return;

        var entry = Data.ConfigEntry;
        if (entry.BackgroundMode == BackgroundMode.Image && entry.EnableImageMask)
        {
            var alpha = (byte)(entry.ImageMaskOpacity * 255);
            _backgroundMaskLayer.Background = new SolidColorBrush(
                Color.FromArgb(alpha, entry.ImageMaskColor.R, entry.ImageMaskColor.G, entry.ImageMaskColor.B));
        }
        else
        {
            _backgroundMaskLayer.Background = null;
        }
    }

    private void EnsureBackgroundLayers()
    {
        if (RootBorder == null) return;
        if (_backgroundImageLayer != null) return;

        var layoutTransformControl = RootBorder.Child as LayoutTransformControl;
        var content = layoutTransformControl?.Child ?? RootBorder.Child;
        if (content is not DockPanel dockPanel) return;

        _backgroundImageLayer = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        _backgroundMaskLayer = new Border
        {
            IsHitTestVisible = false
        };

        var panel = new Panel();
        panel.Children.Add(_backgroundImageLayer);
        panel.Children.Add(_backgroundMaskLayer);
        panel.Children.Add(dockPanel);

        if (layoutTransformControl != null)
            layoutTransformControl.Child = panel;
        else
            RootBorder.Child = panel;
    }

    private void ClearBackgroundLayers()
    {
        if (_backgroundImageLayer != null)
        {
            _backgroundImageLayer.Source = null;
            _backgroundImageLayer.Effect = null;
        }

        if (_backgroundMaskLayer != null)
            _backgroundMaskLayer.Background = null;

        if (RootBorder != null)
            RootBorder.ClearValue(Border.BackgroundProperty);
    }

    private void UpdateImageBlurEffect(double imageBlurRadius)
    {
        if (_backgroundImageLayer == null) return;

        var radius = imageBlurRadius * 20;
        if (radius <= 0.5)
        {
            _backgroundImageLayer.Effect = null;
            return;
        }

        _backgroundImageLayer.Effect = new BlurEffect { Radius = radius };
    }

    private Bitmap GetOrCreateOriginalBackground(string path)
    {
        if (_cachedOriginalBackground != null && _cachedBackgroundPath == path)
            return _cachedOriginalBackground;

        _cachedOriginalBackground?.Dispose();
        using var sk = DecodeBackground(path);
        _cachedOriginalBackground = CreateAvaloniaBitmapFromSkBitmap(sk);
        _cachedBackgroundPath = path;
        return _cachedOriginalBackground;
    }

    private void ClearOriginalBackgroundCache()
    {
        _cachedOriginalBackground?.Dispose();
        _cachedOriginalBackground = null;
        _cachedBackgroundPath = null;
    }

    private static Bitmap CreateAvaloniaBitmapFromSkBitmap(SKBitmap bitmap)
    {
        return new Bitmap(
            PixelFormat.Bgra8888,
            AlphaFormat.Premul,
            bitmap.GetPixels(),
            new PixelSize(bitmap.Width, bitmap.Height),
            new Vector(96, 96),
            bitmap.RowBytes);
    }

    private SKBitmap DecodeBackground(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException("无法读取背景图片。");
        var source = codec.Info;
        var renderScale = RenderScaling;
        var targetWidth = Math.Max(1, Bounds.Width * renderScale * 1.25);
        var targetHeight = Math.Max(1, Bounds.Height * renderScale * 1.25);
        var scale = Math.Min(1, Math.Max(targetWidth / source.Width, targetHeight / source.Height));
        var dimensions = codec.GetScaledDimensions((float)scale);
        var info = new SKImageInfo(Math.Max(1, dimensions.Width), Math.Max(1, dimensions.Height),
            SKColorType.Bgra8888, SKAlphaType.Premul, source.ColorSpace);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput)
            return bitmap;

        bitmap.Dispose();
        throw new InvalidDataException($"背景图片解码失败：{result}");
    }
}
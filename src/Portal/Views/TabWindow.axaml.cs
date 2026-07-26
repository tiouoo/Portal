using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
#if DEBUG
using HotAvalonia;
#endif
using Portal.Classes.Entries;
using Portal.Const;
using Portal.Module.DefaultPage;
using Portal.Module.DragDrop;
using Portal.Views.Components;
using Portal.Views.Pages;
using SkiaSharp;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Helper;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views;

public partial class TabWindow : TioTabWindowBase
{
    private Bitmap? _ownedBackgroundBitmap;
    private bool _isConfigEntrySubscribed;
    private IntPtr _macOsWindowHandle;
    private PixelSize _backgroundPixelSize;

    public bool IsTabMaskVisible
    {
        get;
        set => SetField(ref field, value);
    }

    public TabWindow()
    {
        Build();
    }

    private void Build()
    {
        InitializeComponent();
        Notification = new TioNotificationManager(this);
        Toast = new TioToastManager(this);
        Window = this;
        DataContext = this;
        Events();
        Keys();
        AttachDropDrag();
        CreateNewTabFunc = () =>
        {
            var tab = new TabEntry(this, new NewTabPage())
            {
                IconHeight = 17,
                IconWidth = 17,
                IconMargin = new Thickness(0, 0, 4, -1)
            };
            AddTab(tab);
            SelectTab(tab);
            NavScrollViewer.Offset = new Vector(double.PositiveInfinity, 0);
        };
        if (IsMainWindow)
        {
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
        {
            TabSelectionList.EnableTabDragDrop(this);
        }
        else
        {
            TabSelectionList.PointerPressed += (_, e) =>
            {
                if (!e.Properties.IsLeftButtonPressed) return;
                BeginMoveDrag(e);
            };
        }

        Loaded += (_, _) => ApplyBackground();
    }

#if DEBUG
    [AvaloniaHotReload]
#endif
    public void Hot()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            TabSelectionList.EnableTabDragDrop(this);
        }
    }

    public TabWindow(bool isMainWindow)
    {
        IsMainWindow = isMainWindow;
        Build();
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
        return;

        void MacOsWindowHandler(IntPtr nsWindow)
        {
            try
            {
                TioUi.Common.Helpers.MacOsWindowHandler.RefreshTitleBarButtonPosition(nsWindow, x: 16, y: -3,
                    spacing: 23);
                // TioUi.Common.Helpers.MacOsWindowHandler.HideZoomButton(nsWindow);
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
            TioUi.Common.Helpers.MacOsWindowHandler.RefreshTitleBarButtonPosition(_macOsWindowHandle, x: 16, y: -3,
                spacing: 23);
            // TioUi.Common.Helpers.MacOsWindowHandler.HideZoomButton(_macOsWindowHandle);
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

        // 拖拽行为持有窗口与标签页容器，必须显式分离，否则整个窗口无法被回收。
        TabSelectionList.DisableTabDragDrop();

        this.RemoveHandler(DragDrop.DragLeaveEvent, OnLeaveHandler);
        this.RemoveHandler(DragDrop.DragOverEvent, OnDragHandler);
        this.RemoveHandler(DragDrop.DropEvent, OnDropHandler);

        SizeChanged -= TabWindow_OnSizeChanged;
        Closed -= TabWindow_OnClosed;

        ClearOwnedBackground();
        DataContext = null;
    }

    private void TabWindow_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (Data.ConfigEntry.BackgroundMode != BackgroundMode.Image || _backgroundPixelSize.Width <= 0)
            return;

        var scale = RenderScaling;
        var width = (int)Math.Ceiling(e.NewSize.Width * scale);
        var height = (int)Math.Ceiling(e.NewSize.Height * scale);
        if (width > _backgroundPixelSize.Width * 1.5 || height > _backgroundPixelSize.Height * 1.5)
            ApplyBackground();
    }

    private void OpenAggregatedSearchDialog()
    {
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
        KeyBindings.Add(new KeyBinding
        {
            Gesture = KeyGesture.Parse("Ctrl+Shift+Q"),
            Command = new RelayCommand(() => Data.ConfigEntry.Theme = Data.ConfigEntry.Theme switch
            {
                TioUi.Shared.Theme.Light => TioUi.Shared.Theme.Dark,
                TioUi.Shared.Theme.Dark => TioUi.Shared.Theme.Mirage,
                _ => TioUi.Shared.Theme.Light
            })
        });
#if DEBUG
        KeyBindings.Add(new KeyBinding
        {
            Gesture = KeyGesture.Parse("Shift+F12"),
            Command = new RelayCommand(() =>
            {
                var tioTabWindowBase = this.GetTopLevel() as TioTabWindowBase;
                var tabEntry = new TabEntry(tioTabWindowBase!, new DebugPage());
                tioTabWindowBase.CreateTab(tabEntry);
                tioTabWindowBase.SelectTab(tabEntry);
            })
        });
#endif
        KeyBindings.Add(new KeyBinding
        {
            Gesture = KeyGesture.Parse("Shift+S"),
            Command = new RelayCommand(OpenAggregatedSearchDialog)
        });
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

        // this.AddHandler(DragDrop.DragEnterEvent, OnDragHandler);
        this.AddHandler(DragDrop.DragLeaveEvent, OnLeaveHandler);
        this.AddHandler(DragDrop.DragOverEvent, OnDragHandler);
        this.AddHandler(DragDrop.DropEvent, OnDropHandler);
    }

    private void OnDragHandler(object? sender, DragEventArgs e)
    {
        BarComponent.DropMsg = Handler.GetMsg(e);
    }

    private void OnLeaveHandler(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        BarComponent.DropMsg = null;
    }

    private void OnDropHandler(object? sender, DragEventArgs e)
    {
        BarComponent.DropMsg = null;
        Handler.Handle(e, this);
    }

    public static void ApplyBackgroundToAllWindows()
    {
        foreach (var windowBase in AllWindows)
        {
            if (windowBase is TabWindow tabWin)
                tabWin.ApplyBackground();
        }
    }

    public void ApplyBackground()
    {
        var entry = Data.ConfigEntry;

        switch (entry.BackgroundMode)
        {
            case BackgroundMode.Default:
                if (RootBorder != null)
                    ClearOwnedBackground();
                ClearValue(BackgroundProperty);
                ClearValue(TransparencyBackgroundFallbackProperty);
                TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
                break;

            case BackgroundMode.Image:
                if (RootBorder != null)
                {
                    if (!string.IsNullOrEmpty(entry.BackgroundImagePath) && File.Exists(entry.BackgroundImagePath))
                    {
                        try
                        {
                            using var original = DecodeBackground(entry.BackgroundImagePath);
                            var blurRadius = entry.ImageBlurRadius * 20;
                            if (blurRadius > 0.5)
                            {
                                using var surface = SKSurface.Create(new SKImageInfo(original.Width, original.Height));
                                var canvas = surface.Canvas;
                                using var paint = new SKPaint
                                {
                                    ImageFilter = SKImageFilter.CreateBlur((float)blurRadius, (float)blurRadius)
                                };
                                canvas.DrawBitmap(original, 0, 0, new SKSamplingOptions(), paint);
                                using var blurredImage = surface.Snapshot();
                                using var data = blurredImage.Encode(SKEncodedImageFormat.Png, 80);
                                SetOwnedBackground(new Bitmap(data.AsStream()));
                            }
                            else
                            {
                                using var image = SKImage.FromBitmap(original);
                                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                                SetOwnedBackground(new Bitmap(data.AsStream()));
                            }
                        }
                        catch
                        {
                            ClearOwnedBackground();
                        }
                    }
                    else
                    {
                        ClearOwnedBackground();
                    }
                }

                ClearValue(TransparencyBackgroundFallbackProperty);
                TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
                break;

            case BackgroundMode.Color:
                ClearOwnedBackground();
                if (RootBorder != null)
                    RootBorder.Background = new SolidColorBrush(entry.BackgroundSolidColor);
                ClearValue(TransparencyBackgroundFallbackProperty);
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
                break;

            case BackgroundMode.Acrylic:
                ClearOwnedBackground();
                var color = entry.BackgroundSolidColor;
                var alpha = (byte)(entry.AcrylicOpacity * 255);
                var acrylicBrush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
                Background = Brushes.Transparent;
                if (RootBorder != null)
                    RootBorder.Background = acrylicBrush;
                TransparencyBackgroundFallback = new SolidColorBrush(Color.FromArgb(255, color.R, color.G, color.B));
                TransparencyLevelHint = new[] { WindowTransparencyLevel.AcrylicBlur };
                break;

            case BackgroundMode.Blur:
                ClearOwnedBackground();
                var blurColor = entry.BackgroundSolidColor;
                var blurAlpha = (byte)(entry.BlurOpacity * 255);
                var blurBrush = new SolidColorBrush(Color.FromArgb(blurAlpha, blurColor.R, blurColor.G, blurColor.B));
                Background = Brushes.Transparent;
                if (RootBorder != null)
                    RootBorder.Background = blurBrush;
                TransparencyBackgroundFallback =
                    new SolidColorBrush(Color.FromArgb(255, blurColor.R, blurColor.G, blurColor.B));
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Blur };
                break;

            case BackgroundMode.Mica:
                ClearOwnedBackground();
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
    }

    private void SetOwnedBackground(Bitmap bitmap)
    {
        var oldBitmap = _ownedBackgroundBitmap;
        _ownedBackgroundBitmap = bitmap;
        _backgroundPixelSize = bitmap.PixelSize;
        RootBorder.Background = new ImageBrush(bitmap)
        {
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center
        };
        DisposeBitmapAfterRender(oldBitmap);
    }

    private void ClearOwnedBackground()
    {
        RootBorder?.ClearValue(Border.BackgroundProperty);
        DisposeBitmapAfterRender(_ownedBackgroundBitmap);
        _ownedBackgroundBitmap = null;
        _backgroundPixelSize = default;
    }

    private static void DisposeBitmapAfterRender(Bitmap? bitmap)
    {
        if (bitmap != null)
            Dispatcher.UIThread.Post(bitmap.Dispose, DispatcherPriority.Background);
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
            source.ColorType, source.AlphaType, source.ColorSpace);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput)
            return bitmap;

        bitmap.Dispose();
        throw new InvalidDataException($"背景图片解码失败：{result}");
    }
}

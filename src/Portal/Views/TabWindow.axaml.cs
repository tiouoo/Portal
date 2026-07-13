using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using HotAvalonia;
using Portal.Classes.Entries;
using Portal.Classes.Enums;
using Portal.Const;
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
            var tab = Data.ConfigEntry.DefaultPage switch
            {
                DefaultPage.NewTabPage => new TabEntry(this, new NewTabPage()),
                DefaultPage.SettingPage => new TabEntry(this, new SettingPage()),
                _ => new TabEntry(this, new NewTabPage())
            };
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

    [AvaloniaHotReload]
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

    private DateTime _lastShiftDown;
    private const int DoubleShiftInterval = 280;
    private bool _doubleShiftLock;

    private void Events()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var platform = TryGetPlatformHandle();
            if (platform is null) return;
            var nsWindow = platform.Handle;
            if (nsWindow == IntPtr.Zero) return;
            Loaded += (_, _) => { MacOsWindowHandler(nsWindow); };
            // SizeChanged += (_, _) => { MacOsWindowHandler(nsWindow); };
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(WindowState)) return;
                MacOsWindowHandler(nsWindow);
            };
            SizeChanged += (_, _) => { MacOsWindowHandler(nsWindow); };
            TitleBarThings.SizeChanged += (_, _) =>
            {
                NavScrollViewer.Margin =
                    new Thickness(TitleBarLogo.Bounds.Width + 72, -44, TitleBarThings.Bounds.Width + 15, 0);
            };
        }
        else
        {
            TitleBarThings.SizeChanged += (_, _) =>
            {
                NavScrollViewer.Margin = new Thickness(TitleBarLogo.Bounds.Width + 3, -44,
                    90 + TitleBarThings.Bounds.Width, 0);
            };
        }

        KeyDown += OnWindowKeyDown_CheckDoubleShift;
        NavScrollViewer.ScrollChanged += (_, _) => { IsTabMaskVisible = NavScrollViewer.Offset.X > 0; };
        return;

        void MacOsWindowHandler(IntPtr nsWindow)
        {
            try
            {
                TioUi.Common.Helpers.MacOsWindowHandler.RefreshTitleBarButtonPosition(nsWindow, x: 19, y: -3,
                    spacing: 25);
                TioUi.Common.Helpers.MacOsWindowHandler.HideZoomButton(nsWindow);
            }
            catch (Exception exception)
            {
                Logger.Error(exception);
            }
        }
    }

    private void OnWindowKeyDown_CheckDoubleShift(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.LeftShift and not Key.RightShift)
            return;

        var now = DateTime.Now;
        if (_doubleShiftLock)
            return;

        if ((now - _lastShiftDown).TotalMilliseconds <= DoubleShiftInterval)
        {
            _doubleShiftLock = true;

            OpenAggregatedSearchDialog();

            _lastShiftDown = DateTime.MinValue;
            Task.Run(async () =>
            {
                await Task.Delay(300);
                _doubleShiftLock = false;
            });
        }
        else
        {
            _lastShiftDown = now;
        }
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
            DialogWindowMinWidth = 680,
            DialogWindowMinHeight = 440
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
                    RootBorder.ClearValue(Border.BackgroundProperty);
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
                            using var original = SKBitmap.Decode(entry.BackgroundImagePath);
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
                                RootBorder.Background = new ImageBrush(new Bitmap(data.AsStream()))
                                {
                                    Stretch = Stretch.UniformToFill,
                                    AlignmentX = AlignmentX.Center,
                                    AlignmentY = AlignmentY.Center
                                };
                            }
                            else
                            {
                                RootBorder.Background = new ImageBrush(new Bitmap(entry.BackgroundImagePath))
                                {
                                    Stretch = Stretch.UniformToFill,
                                    AlignmentX = AlignmentX.Center,
                                    AlignmentY = AlignmentY.Center
                                };
                            }
                        }
                        catch
                        {
                            RootBorder.ClearValue(Border.BackgroundProperty);
                        }
                    }
                    else
                    {
                        RootBorder.ClearValue(Border.BackgroundProperty);
                    }
                }
                ClearValue(TransparencyBackgroundFallbackProperty);
                TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
                break;

            case BackgroundMode.Color:
                if (RootBorder != null)
                    RootBorder.Background = new SolidColorBrush(entry.BackgroundSolidColor);
                ClearValue(TransparencyBackgroundFallbackProperty);
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
                break;

            case BackgroundMode.Acrylic:
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
                var blurColor = entry.BackgroundSolidColor;
                var blurAlpha = (byte)(entry.BlurOpacity * 255);
                var blurBrush = new SolidColorBrush(Color.FromArgb(blurAlpha, blurColor.R, blurColor.G, blurColor.B));
                Background = Brushes.Transparent;
                if (RootBorder != null)
                    RootBorder.Background = blurBrush;
                TransparencyBackgroundFallback = new SolidColorBrush(Color.FromArgb(255, blurColor.R, blurColor.G, blurColor.B));
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Blur };
                break;

            case BackgroundMode.Mica:
                var micaColor = entry.BackgroundSolidColor;
                var micaAlpha = (byte)(entry.MicaOpacity * 255);
                var micaBrush = new SolidColorBrush(Color.FromArgb(micaAlpha, micaColor.R, micaColor.G, micaColor.B));
                Background = Brushes.Transparent;
                if (RootBorder != null)
                    RootBorder.Background = micaBrush;
                TransparencyBackgroundFallback = new SolidColorBrush(Color.FromArgb(255, micaColor.R, micaColor.G, micaColor.B));
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Mica };
                break;
        }
    }
}
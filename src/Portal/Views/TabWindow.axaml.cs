using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using HotAvalonia;
using Portal.Const;
using Portal.Module.DragDrop;
using Portal.Views.Components;
using Portal.Views.Pages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Controls;

namespace Portal.Views;

public partial class TabWindow : TioTabWindowBase
{
    public bool IsTabMaskVisible
    {
        get;
        set => SetField(ref field, value);
    }

    int _index = 1;

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
            var tab = new TabEntry(this, new NewTabPage(), header: $"new tab {_index}");
            _index++;
            AddTab(tab);
            SelectTab(tab);
            NavScrollViewer.Offset = new Vector(double.PositiveInfinity, 0);
        };
        if (IsMainWindow)
        {
            CreateNewTabFunc();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            TabSelectionList.PointerPressed += (_, e) =>
            {
                if (!e.Properties.IsLeftButtonPressed) return;
                BeginMoveDrag(e);
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            TabSelectionList.EnableTabDragDrop(this);
        }
    }

    [AvaloniaHotReload]
    public void Hot()
    {
        TabSelectionList.EnableTabDragDrop(this);
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
            TitleBarThings.SizeChanged += (_, _) =>
            {
                NavScrollViewer.Margin =
                    new Thickness(TitleBarLogo.Bounds.Width + 10, -44, TitleBarThings.Bounds.Width, 0);
            };
        }
        else
        {
            TitleBarThings.SizeChanged += (_, _) =>
            {
                NavScrollViewer.Margin = new Thickness(TitleBarLogo.Bounds.Width + 10, -44,
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

        this.AddHandler(DragDrop.DragEnterEvent, OnDragHandler);
        this.AddHandler(DragDrop.DragOverEvent, OnDragHandler);
        this.AddHandler(DragDrop.DropEvent, OnDropHandler);
    }

    private void OnDragHandler(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
    }

    private void OnDropHandler(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
        Handler.Handle(e, this);
    }
}
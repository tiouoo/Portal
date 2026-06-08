using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Portal.Const;
using Portal.Views.Pages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Platform;
using Tio.Avalonia.Standard.Standard.Ui;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common.Helpers;
using TioUi.Controls;

namespace Portal.Views;

public partial class MainWindow : TioTabWindowBase
{
    public bool IsTabMaskVisible
    {
        get;
        set => SetField(ref field, value);
    }

    public MainWindow()
    {
        InitializeComponent();
        Notification = new TioNotificationManager(this);
        Toast = new TioToastManager(this);
        Window = this;
        IsMainWindow = true;
        DataContext = this;
        Events();
        Keys();
        NavScrollViewer.ScrollChanged += (_, _) => { IsTabMaskVisible = NavScrollViewer.Offset.X > 0; };
        CreateNewTabFunc = () =>
        {
            var tab = new TabEntry(this, new NewTabPage());
            AddTab(tab);
            SelectTab(tab);
        };
    }

    private void Events()
    {
        if (Data.DesktopType == DesktopType.MacOs)
        {
            var platform = TryGetPlatformHandle();
            if (platform is null) return;
            var nsWindow = platform.Handle;
            if (nsWindow == IntPtr.Zero) return;
            PropertyChanged += (_, e) =>
            {
                try
                {
                    MacOsWindowHandler.RefreshTitleBarButtonPosition(nsWindow);
                    MacOsWindowHandler.HideZoomButton(nsWindow);
                }
                catch (Exception exception)
                {
                    Logger.Error(exception);
                }
            };
        }
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
        Tabs.Add(new TabEntry(this, new NewTabPage()));
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed) return;
        var c = ((Border)sender).Tag as TabEntry;
        c.Close();
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
}
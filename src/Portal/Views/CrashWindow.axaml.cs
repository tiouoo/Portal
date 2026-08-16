using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Tio.Avalonia.Standard.Modules.DiskIO;
using TioUi.Common.Helpers;
using TioUi.Controls;

namespace Portal.Views;

public partial class CrashWindow : TioWindow
{
    public CrashWindow() : this(string.Empty)
    {
    }

    public CrashWindow(string e)
    {
        InitializeComponent();
        SelectableTextBlock.Text = e;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var nsWindow = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (nsWindow == IntPtr.Zero) return;
            Loaded += (_, _) => RefreshMacOsTitleBarButtons(nsWindow);
            PropertyChanged += (_, args) =>
            {
                if (args.Property.Name != nameof(WindowState)) return;
                RefreshMacOsTitleBarButtons(nsWindow);
            };
            SizeChanged += (_, _) => RefreshMacOsTitleBarButtons(nsWindow);
        }
    }

    private static void RefreshMacOsTitleBarButtons(IntPtr nsWindow)
    {
        try
        {
            MacOsWindowHandler.RefreshTitleBarButtonPosition(nsWindow, 14, 2,
                20);
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
        }
    }

    private void Restart_OnClick(object? sender, RoutedEventArgs e)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.CurrentDirectory,
            FileName = Process.GetCurrentProcess().MainModule.FileName
        };
        Process.Start(startInfo);
        Environment.Exit(0);
    }

    private void Exit_OnClick(object? sender, RoutedEventArgs e)
    {
        Environment.Exit(0);
    }

    private void Continue_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        var clipboard = GetTopLevel(this)?.Clipboard;
        clipboard?.SetTextAsync(SelectableTextBlock.Text);
    }
}
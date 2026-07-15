using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Portal.Bedrock.Standard.Manifest;

namespace Portal.Bedrock;

internal sealed class ProcessMouseLocker : IDisposable
{
    private const uint GaRoot = 2;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkShift = 0x10;
    private const int VkLwin = 0x5B;
    private const int VkRwin = 0x5C;

    private readonly Process _process;
    private readonly int _processId;
    private readonly int _inset;
    private readonly Hotkey _hotkey;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _monitorTask;
    private bool _manuallyUnlocked;
    private bool _hotkeyWasPressed;
    private bool _cursorIsClipped;
    private bool _disposed;

    public ProcessMouseLocker(Process process, BedrockInstanceConfig config)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(config);

        _process = process;
        _processId = process.Id;
        _inset = Math.Max(0, config.MouseLockInset);
        _hotkey = Hotkey.Parse(config.MouseLockHotkey);
        _monitorTask = Task.Run(MonitorAsync);
    }

    private async Task MonitorAsync()
    {
        try
        {
            while (!_cancellation.IsCancellationRequested && !HasExited())
            {
                var hotkeyPressed = _hotkey.IsPressed();
                if (hotkeyPressed && !_hotkeyWasPressed)
                {
                    _manuallyUnlocked = !_manuallyUnlocked;
                    if (_manuallyUnlocked)
                        ReleaseCursor();
                }
                _hotkeyWasPressed = hotkeyPressed;

                if (!_manuallyUnlocked && TryGetForegroundGameWindow(out var gameWindow) &&
                    TryGetClipRectangle(gameWindow, out var clipRectangle))
                {
                    if (clipRectangle.Right > clipRectangle.Left && clipRectangle.Bottom > clipRectangle.Top &&
                        ClipCursor(ref clipRectangle))
                        _cursorIsClipped = true;
                }
                else
                {
                    ReleaseCursor();
                }

                await Task.Delay(50, _cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch
        {
            // A failed monitor must never leave a process-wide cursor clip behind.
        }
        finally
        {
            ReleaseCursor(force: true);
        }
    }

    private bool TryGetForegroundGameWindow(out IntPtr gameWindow)
    {
        gameWindow = IntPtr.Zero;
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return false;

        var root = GetAncestor(foreground, GaRoot);
        if (root == IntPtr.Zero)
            root = foreground;

        if (GetWindowProcessId(root) == _processId)
        {
            gameWindow = root;
            return true;
        }

        if (!string.Equals(GetWindowClass(root), "ApplicationFrameWindow", StringComparison.Ordinal))
            return false;

        var containsTargetProcess = false;
        EnumChildWindows(root, (child, _) =>
        {
            if (GetWindowProcessId(child) != _processId)
                return true;

            containsTargetProcess = true;
            return false;
        }, IntPtr.Zero);

        if (!containsTargetProcess)
            return false;

        gameWindow = root;
        return true;
    }

    private bool TryGetClipRectangle(IntPtr window, out Rect rectangle)
    {
        rectangle = default;
        if (!GetClientRect(window, out var client))
            return false;

        var topLeft = client.TopLeft;
        var bottomRight = client.BottomRight;
        if (!ClientToScreen(window, ref topLeft) || !ClientToScreen(window, ref bottomRight))
            return false;
        client.TopLeft = topLeft;
        client.BottomRight = bottomRight;

        rectangle = new Rect
        {
            Left = client.Left + _inset,
            Top = client.Top + _inset,
            Right = client.Right - _inset,
            Bottom = client.Bottom - _inset
        };
        return true;
    }

    private bool HasExited()
    {
        try
        {
            return _process.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private void ReleaseCursor(bool force = false)
    {
        if (!force && !_cursorIsClipped)
            return;

        ClipCursor(IntPtr.Zero);
        _cursorIsClipped = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cancellation.Cancel();
        ReleaseCursor(force: true);
        try
        {
            _monitorTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException exception) when (exception.InnerExceptions.All(
                   inner => inner is OperationCanceledException))
        {
        }
        finally
        {
            ReleaseCursor(force: true);
            _cancellation.Dispose();
        }
    }

    private static int GetWindowProcessId(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var processId);
        return unchecked((int)processId);
    }

    private static string GetWindowClass(IntPtr window)
    {
        var className = new StringBuilder(256);
        return GetClassName(window, className, className.Capacity) > 0 ? className.ToString() : string.Empty;
    }

    private readonly record struct Hotkey(bool Control, bool Alt, bool Shift, bool Win, int? Key)
    {
        private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Escape"] = 0x1B,
            ["Esc"] = 0x1B,
            ["Space"] = 0x20,
            ["Tab"] = 0x09,
            ["Enter"] = 0x0D,
            ["Backspace"] = 0x08,
            ["Delete"] = 0x2E,
            ["Insert"] = 0x2D,
            ["Home"] = 0x24,
            ["End"] = 0x23,
            ["PageUp"] = 0x21,
            ["PageDown"] = 0x22,
            ["Up"] = 0x26,
            ["Down"] = 0x28,
            ["Left"] = 0x25,
            ["Right"] = 0x27
        };

        public static Hotkey Parse(string? value)
        {
            var control = false;
            var alt = false;
            var shift = false;
            var win = false;
            var isValid = true;
            int? key = null;

            foreach (var rawPart in (value ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
            {
                if (rawPart.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    rawPart.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    control = true;
                else if (rawPart.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    alt = true;
                else if (rawPart.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    shift = true;
                else if (rawPart.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                         rawPart.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                    win = true;
                else if (TryParseKey(rawPart, out var parsedKey))
                    key = parsedKey;
                else
                    isValid = false;
            }

            return isValid && (control || alt || shift || win || key.HasValue)
                ? new Hotkey(control, alt, shift, win, key)
                : new Hotkey(true, true, false, false, null);
        }

        public bool IsPressed()
        {
            var controlDown = IsKeyDown(VkControl);
            var altDown = IsKeyDown(VkMenu);
            var shiftDown = IsKeyDown(VkShift);
            var winDown = IsKeyDown(VkLwin) || IsKeyDown(VkRwin);
            return controlDown == Control && altDown == Alt && shiftDown == Shift && winDown == Win &&
                   (!Key.HasValue || IsKeyDown(Key.Value));
        }

        private static bool TryParseKey(string value, out int key)
        {
            if (value.Length == 1 && char.IsAsciiLetterOrDigit(value[0]))
            {
                key = char.ToUpperInvariant(value[0]);
                return true;
            }

            if (value.Length is >= 2 and <= 3 && value[0] is 'F' or 'f' &&
                int.TryParse(value.AsSpan(1), out var functionKey) && functionKey is >= 1 and <= 24)
            {
                key = 0x70 + functionKey - 1;
                return true;
            }

            return NamedKeys.TryGetValue(value, out key);
        }

        private static bool IsKeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public Point TopLeft
        {
            get => new() { X = Left, Y = Top };
            set { Left = value.X; Top = value.Y; }
        }

        public Point BottomRight
        {
            get => new() { X = Right, Y = Bottom };
            set { Right = value.X; Bottom = value.Y; }
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out Rect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr window, ref Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClipCursor(ref Rect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClipCursor(IntPtr rectangle);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}

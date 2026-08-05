using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Views.Pages;

namespace Portal.Views.SubWindows;

public partial class OverlayWindow : Window
{
    private readonly DispatcherTimer _clockTimer;
    private readonly TextBlock _timeBlock;
    private readonly MinecraftInstance _instance;
    // --- Win32 常量 ---
    private const int GWL_EXSTYLE = -20;
    private const int GWL_STYLE = -16;
    private const int WS_CHILD = 0x40000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOZORDER = 0x0004;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int HWND_TOP = 0;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_SHIFT = 0x10;
    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;

    // --- 成员变量 ---
    private readonly LowLevelKeyboardProc _proc;
    private readonly Process? _targetProcess;
    private IntPtr _hookID = IntPtr.Zero;

    // 动画控制
    private volatile bool _isAnimating;
    private bool _isEmbedded;
    private bool _isOverlayVisible;

    // UWP特定变量
    private bool _isUWPApp;
    private IntPtr _myHandle = IntPtr.Zero;
    private bool _nextAction;
    private int _originalHeight;
    private int _originalWidth;
    private int _originalX;
    private int _originalY;
    private volatile bool _pendingAction;
    private DispatcherTimer? _syncTimer;
    private IntPtr _targetHwnd = IntPtr.Zero;

    public OverlayWindow(Process targetProcess, MinecraftInstance instance)
    {
        InitializeComponent();

        _targetProcess = targetProcess;
        _instance = instance;

        _timeBlock = TimeBlock;

        VersionBox.Text = !string.IsNullOrEmpty(Data.Instance.Version.VersionTitle)
            ? $"Portal {Data.Instance.Version.VersionTitle}"
            : "Portal";

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => _timeBlock.Text = DateTime.Now.ToString("HH:mm:ss");
        _clockTimer.Start();

        _proc = HookCallback;

        Topmost = true;

        Opened += OnWindowOpened;
        Closed += OnWindowClosed;
    }

    // --- Win32 API 导入 ---
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, ref RECT rectangle);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder strText, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, ref RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        var platformHandle = TryGetPlatformHandle();
        if (platformHandle == null) return;
        _myHandle = platformHandle.Handle;

        InitializeHiddenState();

        Task.Run(async () =>
        {
            for (var i = 0; i < 20; i++)
            {
                try
                {
                    var foundHwnd = FindTargetWindow();

                    if (foundHwnd != IntPtr.Zero)
                    {
                        Dispatcher.UIThread.Invoke(() =>
                        {
                            _targetHwnd = foundHwnd;
                            _isUWPApp = IsUWPWindow(foundHwnd);
                            InitializeOverlay();
                        });
                        break;
                    }
                }
                catch (Exception exception)
                {
                    Debug.WriteLine(exception);
                }

                await Task.Delay(1000);
            }

            if (_targetHwnd == IntPtr.Zero)
                Dispatcher.UIThread.Invoke(() =>
                {
                    Debug.WriteLine("无法找到目标进程的窗口");
                });
        });
    }

    private IntPtr FindTargetWindow()
    {
        if (_targetProcess == null) return IntPtr.Zero;

        IntPtr foundHandle = IntPtr.Zero;

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            StringBuilder sbClass = new StringBuilder(256);
            GetClassName(hWnd, sbClass, 256);
            string className = sbClass.ToString();

            if (className.Contains("ConsoleWindowClass") || className.Contains("Ghost"))
                return true;

            GetWindowThreadProcessId(hWnd, out uint pid);

            if (pid == _targetProcess.Id)
            {
                foundHandle = hWnd;
                return false;
            }

            if (className == "ApplicationFrameWindow")
            {
                bool isMatch = false;
                EnumChildWindows(hWnd, (childHwnd, l) =>
                {
                    GetWindowThreadProcessId(childHwnd, out uint childPid);
                    if (childPid == _targetProcess.Id)
                    {
                        isMatch = true;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);

                if (isMatch)
                {
                    foundHandle = hWnd;
                    return false;
                }
            }

            return true;
        }, IntPtr.Zero);

        return foundHandle;
    }

    private void InitializeHiddenState()
    {
        _isOverlayVisible = false;
        _isEmbedded = false;

        var exStyle = GetWindowLong(_myHandle, GWL_EXSTYLE);
        exStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        exStyle &= ~WS_EX_LAYERED;
        SetWindowLong(_myHandle, GWL_EXSTYLE, exStyle);

        ShowWindow(_myHandle, SW_HIDE);
        EnableWindow(_myHandle, false);

        IsHitTestVisible = false;
        OverlayRoot.IsHitTestVisible = false;
    }

    private bool IsUWPWindow(IntPtr hwnd)
    {
        var className = GetClassName(hwnd);

        if (className.Contains("ApplicationFrame") ||
            className.Contains("Windows.UI.Core") ||
            className.Contains("Windows.UI.Xaml"))
            return true;

        GetWindowThreadProcessId(hwnd, out var pid);
        var process = Process.GetProcessById((int)pid);
        if (process.ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    private string GetClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        var length = GetClassName(hWnd, sb, sb.Capacity);

        if (length == 0)
        {
            var errorCode = Marshal.GetLastWin32Error();
            if (errorCode != 0)
            {
                Debug.WriteLine($"GetClassName failed with error code: {errorCode}");
                return string.Empty;
            }
        }

        return sb.ToString(0, Math.Min(length, 255));
    }

    private string GetWindowText(IntPtr hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length == 0) return "";

        var sb = new StringBuilder(length + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private void InitializeOverlay()
    {
        using (var curProcess = Process.GetCurrentProcess())
        using (var curModule = curProcess.MainModule)
        {
            _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule?.ModuleName), 0);
        }

        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) };
        _syncTimer.Tick += (_, _) => SyncSize();
        _syncTimer.Start();

        SetOverlayState(false);
    }

    private void SetOverlayState(bool visible)
    {
        if (_isAnimating)
        {
            _pendingAction = true;
            _nextAction = visible;
            return;
        }

        if (_isOverlayVisible == visible) return;

        StartAnimation(visible);
    }

    private async void StartAnimation(bool visible)
    {
        _isAnimating = true;

        if (visible)
        {
            ShowOverlayImmediate();
            await AnimateOpacity(0, 1, 100);
        }
        else
        {
            await AnimateOpacity(1, 0, 150);
            HideOverlayInternal();
        }

        _isAnimating = false;

        if (_pendingAction)
        {
            _pendingAction = false;
            SetOverlayState(_nextAction);
        }
    }

    private async Task AnimateOpacity(double startOpacity, double endOpacity, int durationMs)
    {
        const int steps = 10;
        var stepDuration = durationMs / steps;
        var stepValue = (endOpacity - startOpacity) / steps;

        OverlayRoot.Opacity = startOpacity;

        for (var i = 1; i <= steps; i++)
        {
            if (!_isAnimating) break;

            var newOpacity = startOpacity + stepValue * i;
            if (newOpacity < 0) newOpacity = 0;
            if (newOpacity > 1) newOpacity = 1;

            OverlayRoot.Opacity = newOpacity;
            await Task.Delay(stepDuration);
        }

        OverlayRoot.Opacity = endOpacity;
    }

    private void ShowOverlayImmediate()
    {
        _isOverlayVisible = true;

        InstanceBorder.Opacity = 0;
        _isInstanceDetailVisible = false;

        if (_targetHwnd != IntPtr.Zero)
        {
            if (_isUWPApp)
            {
                ShowUWPOverlay();
            }
            else
            {
                SetParent(_myHandle, _targetHwnd);
                _isEmbedded = true;

                var clientRect = new RECT();
                if (GetClientRect(_targetHwnd, ref clientRect))
                {
                    _originalWidth = clientRect.Right - clientRect.Left;
                    _originalHeight = clientRect.Bottom - clientRect.Top;
                    _originalX = clientRect.Left;
                    _originalY = clientRect.Top;
                }

                var style = GetWindowLong(_myHandle, GWL_STYLE);
                SetWindowLong(_myHandle, GWL_STYLE, style | WS_CHILD);

                Width = _originalWidth;
                Height = _originalHeight;
                MoveWindow(_myHandle, _originalX, _originalY, _originalWidth, _originalHeight, true);

                var exStyle = GetWindowLong(_myHandle, GWL_EXSTYLE);
                exStyle &= ~WS_EX_TRANSPARENT;
                exStyle &= ~WS_EX_NOACTIVATE;
                exStyle |= WS_EX_LAYERED;
                SetWindowLong(_myHandle, GWL_EXSTYLE, exStyle);

                ShowWindow(_myHandle, SW_SHOW);
                EnableWindow(_myHandle, true);

                IsHitTestVisible = true;
                OverlayRoot.IsHitTestVisible = true;

                SetForegroundWindow(_myHandle);
                SetFocus(_myHandle);
            }
        }
    }

    private void ShowUWPOverlay()
    {
        _isEmbedded = true;

        var exStyle = GetWindowLong(_myHandle, GWL_EXSTYLE);
        exStyle &= ~WS_EX_TRANSPARENT;
        exStyle &= ~WS_EX_NOACTIVATE;
        exStyle |= WS_EX_LAYERED;
        SetWindowLong(_myHandle, GWL_EXSTYLE, exStyle);

        var windowRect = new RECT();
        if (GetWindowRect(_targetHwnd, ref windowRect))
        {
            _originalWidth = windowRect.Right - windowRect.Left;
            _originalHeight = windowRect.Bottom - windowRect.Top;
            _originalX = windowRect.Left;
            _originalY = windowRect.Top;

            Width = _originalWidth;
            Height = _originalHeight;

            SetWindowPos(_myHandle, _targetHwnd,
                _originalX, _originalY,
                _originalWidth, _originalHeight,
                SWP_NOZORDER | SWP_NOACTIVATE);

            SetWindowPos(_myHandle, HWND_TOP, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        ShowWindow(_myHandle, SW_SHOW);
        EnableWindow(_myHandle, true);

        IsHitTestVisible = true;
        OverlayRoot.IsHitTestVisible = true;

        SetForegroundWindow(_myHandle);
        SetFocus(_myHandle);
    }

    private void HideOverlayInternal()
    {
        _isOverlayVisible = false;

        IsHitTestVisible = false;
        OverlayRoot.IsHitTestVisible = false;
        OverlayRoot.Opacity = 0;

        var exStyle = GetWindowLong(_myHandle, GWL_EXSTYLE);
        exStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        exStyle &= ~WS_EX_LAYERED;
        SetWindowLong(_myHandle, GWL_EXSTYLE, exStyle);

        if (_isEmbedded)
        {
            SetParent(_myHandle, IntPtr.Zero);
            _isEmbedded = false;

            var style = GetWindowLong(_myHandle, GWL_STYLE);
            SetWindowLong(_myHandle, GWL_STYLE, style & ~WS_CHILD);
        }

        if (_targetHwnd != IntPtr.Zero)
        {
            SetForegroundWindow(_targetHwnd);
            SetFocus(_targetHwnd);
        }

        ShowWindow(_myHandle, SW_HIDE);
        EnableWindow(_myHandle, false);
    }

    private void SyncSize()
    {
        if (!_isOverlayVisible || !_isEmbedded || _targetHwnd == IntPtr.Zero || _myHandle == IntPtr.Zero) return;

        if (_isUWPApp)
        {
            SyncUWPSizes();
        }
        else
        {
            var clientRect = new RECT();
            if (GetClientRect(_targetHwnd, ref clientRect))
            {
                var w = clientRect.Right - clientRect.Left;
                var h = clientRect.Bottom - clientRect.Top;

                if (Width != w || Height != h)
                {
                    Width = w;
                    Height = h;
                    MoveWindow(_myHandle, 0, 0, w, h, true);
                }
            }
        }
    }

    private void SyncUWPSizes()
    {
        var windowRect = new RECT();
        if (GetWindowRect(_targetHwnd, ref windowRect))
        {
            var w = windowRect.Right - windowRect.Left;
            var h = windowRect.Bottom - windowRect.Top;

            if (Width != w || Height != h)
            {
                Width = w;
                Height = h;

                SetWindowPos(_myHandle, _targetHwnd,
                    windowRect.Left, windowRect.Top,
                    w, h,
                    SWP_NOZORDER | SWP_NOACTIVATE);
            }
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var vkCode = Marshal.ReadInt32(lParam);

            var isShiftDown = (GetKeyState(VK_SHIFT) & 0x8000) != 0;

            if (vkCode == VK_TAB && isShiftDown)
            {
                var foreground = GetForegroundWindow();
                if (foreground == _targetHwnd || foreground == _myHandle)
                {
                    Dispatcher.UIThread.Post(() => SetOverlayState(!_isOverlayVisible));
                    return (IntPtr)1;
                }
            }

            if (vkCode == VK_ESCAPE)
            {
                var foreground = GetForegroundWindow();
                if (foreground == _myHandle && _isOverlayVisible)
                {
                    Dispatcher.UIThread.Post(() => SetOverlayState(false));
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _clockTimer.Stop();
        _syncTimer?.Stop();
        if (_hookID != IntPtr.Zero) UnhookWindowsHookEx(_hookID);
    }

    private void CloseBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        SetOverlayState(false);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private bool _isInstanceDetailAnimating;
    private bool _isInstanceDetailVisible;

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ToggleInstanceDetail();
    }

    private void ToggleInstanceDetail()
    {
        if (_isInstanceDetailAnimating) return;

        _isInstanceDetailAnimating = true;

        if (InstanceContentControl.Content == null)
        {
            var detailPage = new InstanceDetailPage(_instance);
            InstanceContentControl.Content = detailPage;
        }

        if (_isInstanceDetailVisible)
        {
            HideInstanceDetailAsync();
        }
        else
        {
            ShowInstanceDetailAsync();
        }
    }

    private async void ShowInstanceDetailAsync()
    {
        InstanceBorder.IsHitTestVisible = true;
        InstanceBorder.Opacity = 0;

        var durationMs = 200;
        const int steps = 10;
        var stepDuration = durationMs / steps;
        var stepValue = 1.0 / steps;

        for (var i = 1; i <= steps; i++)
        {
            InstanceBorder.Opacity = stepValue * i;
            await Task.Delay(stepDuration);
        }

        InstanceBorder.Opacity = 1;
        _isInstanceDetailVisible = true;
        _isInstanceDetailAnimating = false;
    }

    private async void HideInstanceDetailAsync()
    {
        var durationMs = 150;
        const int steps = 10;
        var stepDuration = durationMs / steps;
        var stepValue = 1.0 / steps;

        for (var i = 1; i <= steps; i++)
        {
            InstanceBorder.Opacity = 1 - stepValue * i;
            await Task.Delay(stepDuration);
        }

        InstanceBorder.Opacity = 0;
        InstanceBorder.IsHitTestVisible = false;
        _isInstanceDetailVisible = false;
        _isInstanceDetailAnimating = false;
    }

    private void CloseInstance(object? sender, RoutedEventArgs e)
    {
        HideInstanceDetailAsync();
    }
}
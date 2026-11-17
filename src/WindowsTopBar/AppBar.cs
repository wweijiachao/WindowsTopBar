using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace WindowsTopBar;

internal sealed class AppBar : IDisposable
{
    private const uint AbmNew = 0;
    private const uint AbmRemove = 1;
    private const uint AbmQueryPos = 2;
    private const uint AbmSetPos = 3;
    private const uint AbmActivate = 6;
    private const uint AbmWindowPosChanged = 9;
    private const uint AbeTop = 1;

    private const int AbnPosChanged = 1;
    private const int AbnFullscreenApp = 2;
    private const int WmActivate = 0x0006;
    private const int WmWindowPosChanged = 0x0047;
    private const int WmDisplayChange = 0x007E;
    private const int WmDpiChanged = 0x02E0;
    private const uint MonitorDefaultToPrimary = 1;
    private const int DwmExtendedFrameBounds = 9;
    private const int DwmCloaked = 14;
    private const int SnapEdgeToleranceDip = 16;
    private const int GwlExStyle = -20;
    private const int WsExTopmost = 0x00000008;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExAppWindow = 0x00040000;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpAsyncWindowPos = 0x4000;
    private static readonly nint HwndTopmost = -1;
    private static readonly nint HwndNotTopmost = -2;
    private static readonly nint HwndBottom = 1;
    private static readonly IsWindowArrangedCallback? IsWindowArranged = LoadIsWindowArranged();

    private readonly HwndSource _source;
    private readonly nint _handle;
    private readonly double _height;
    private uint _callbackMessage;
    private DispatcherOperation? _pendingPosition;
    private Rect? _requestedBounds;
    private Rect _approvedBounds;
    private bool _registered;
    private double _centerOffset;

    public event Action<double>? CenterOffsetChanged;

    public AppBar(HwndSource source, double height)
    {
        _source = source;
        _handle = source.Handle;
        _height = height;
    }

    public void Start()
    {
        ApplyToolWindowStyle();
        _source.AddHook(OnWindowMessage);
        _callbackMessage = RegisterWindowMessageW("WindowsTopBar.AppBar");
        var data = CreateData();
        data.CallbackMessage = _callbackMessage;
        _registered = _callbackMessage != 0 && SHAppBarMessage(AbmNew, ref data) != 0;
        if (!_registered)
        {
            Trace.TraceWarning("Could not register the Windows AppBar.");
        }

        UpdatePosition();
        if (_registered)
        {
            SetZOrder(HwndTopmost);
        }
    }

    private void ApplyToolWindowStyle()
    {
        var style = GetWindowLongW(_handle, GwlExStyle);
        if (style == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            Trace.TraceWarning("Could not read the top bar window style.");
            return;
        }

        var toolStyle = (style | WsExToolWindow) & ~WsExAppWindow;
        if ((SetWindowLongW(_handle, GwlExStyle, toolStyle) == 0 && Marshal.GetLastPInvokeError() != 0) ||
            !SetWindowPos(_handle, 0, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged))
        {
            Trace.TraceWarning("Could not apply the top bar tool window style.");
        }
    }

    public void Dispose()
    {
        _source.RemoveHook(OnWindowMessage);
        _pendingPosition?.Abort();
        _pendingPosition = null;
        Unregister();
    }

    public void ActivateWindowBelow(int screenX)
    {
        var monitor = MonitorFromWindow(_handle, 0);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfoW(monitor, ref info))
        {
            return;
        }

        var dpi = GetDpiForWindow(_handle);
        var tolerance = (int)Math.Ceiling(SnapEdgeToleranceDip * dpi / 96.0);
        var (target, companion) = SelectWindow(ReadWindowsInZOrder(monitor), info.WorkArea, tolerance, screenX);
        if (target == 0 || !SetForegroundWindow(target))
        {
            return;
        }

        if (companion != 0 && !SetWindowPos(companion, 0, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpAsyncWindowPos))
        {
            Trace.TraceWarning("Could not raise the companion snapped window.");
        }
    }

    private IEnumerable<WindowCandidate> ReadWindowsInZOrder(nint monitor)
    {
        var desktop = GetDesktopWindow();
        var visited = new HashSet<nint>();
        for (var window = FindWindowExW(desktop, 0, null, null); window != 0 && visited.Add(window);
             window = FindWindowExW(desktop, window, null, null))
        {
            if (window == _handle || !IsWindowVisible(window) || !IsWindowEnabled(window) || IsIconic(window) ||
                MonitorFromWindow(window, 0) != monitor ||
                DwmGetWindowAttribute(window, DwmCloaked, out int cloaked, sizeof(int)) < 0 || cloaked != 0)
            {
                continue;
            }

            var style = GetWindowLongW(window, GwlExStyle);
            if ((style == 0 && Marshal.GetLastPInvokeError() != 0) || (style & (WsExToolWindow | WsExNoActivate)) != 0)
            {
                continue;
            }

            if (IsZoomed(window))
            {
                yield return new WindowCandidate(window, Maximized: true, default);
            }
            else if ((style & WsExTopmost) == 0 && (IsWindowArranged?.Invoke(window) ?? 0) != 0 &&
                     DwmGetWindowAttribute(window, DwmExtendedFrameBounds, out Rect bounds, Marshal.SizeOf<Rect>()) >= 0)
            {
                yield return new WindowCandidate(window, Maximized: false, bounds);
            }
        }
    }

    private static (nint Target, nint Companion) SelectWindow(
        IEnumerable<WindowCandidate> windows, Rect workArea, int tolerance, int screenX)
    {
        WindowCandidate left = default;
        WindowCandidate right = default;
        nint maximized = 0;
        var ambiguous = false;
        foreach (var window in windows)
        {
            // A maximized window separates the visible layout from any snapped windows behind it.
            if (window.Maximized)
            {
                maximized = window.Handle;
                break;
            }

            var bounds = window.Bounds;
            if (bounds.Right <= bounds.Left || Math.Abs(bounds.Top - workArea.Top) > tolerance ||
                Math.Abs(bounds.Bottom - workArea.Bottom) > tolerance)
            {
                continue;
            }

            if (Math.Abs(bounds.Left - workArea.Left) <= tolerance && bounds.Right < workArea.Right - tolerance)
            {
                ambiguous |= left.Handle != 0;
                left = window;
            }
            else if (Math.Abs(bounds.Right - workArea.Right) <= tolerance && bounds.Left > workArea.Left + tolerance)
            {
                ambiguous |= right.Handle != 0;
                right = window;
            }
        }

        if (ambiguous || left.Handle == 0 || right.Handle == 0 ||
            Math.Abs(left.Bounds.Right - right.Bounds.Left) > tolerance)
        {
            return (maximized, 0);
        }

        var divider = left.Bounds.Right + (right.Bounds.Left - left.Bounds.Right) / 2;
        return screenX < divider ? (left.Handle, right.Handle) : (right.Handle, left.Handle);
    }

    private static IsWindowArrangedCallback? LoadIsWindowArranged()
    {
        var module = GetModuleHandleW("user32.dll");
        return module != 0 && NativeLibrary.TryGetExport(module, "IsWindowArranged", out var address)
            ? Marshal.GetDelegateForFunctionPointer<IsWindowArrangedCallback>(address)
            : null;
    }

    private void Unregister()
    {
        if (_registered)
        {
            _registered = false;
            _requestedBounds = null;
            var data = CreateData();
            if (SHAppBarMessage(AbmRemove, ref data) == 0)
            {
                Trace.TraceWarning("Could not unregister the Windows AppBar.");
            }
        }
    }

    private nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (_registered && message == _callbackMessage)
        {
            if (wParam == AbnPosChanged)
            {
                QueuePositionUpdate();
            }
            else if (wParam == AbnFullscreenApp)
            {
                SetZOrder(lParam != 0 ? HwndBottom : HwndTopmost);
            }
        }
        else if (message is WmDisplayChange or WmDpiChanged)
        {
            QueuePositionUpdate();
        }
        else if (_registered && (message is WmActivate or WmWindowPosChanged))
        {
            var data = CreateData();
            SHAppBarMessage(message == WmActivate ? AbmActivate : AbmWindowPosChanged, ref data);
        }

        return 0;
    }

    private void QueuePositionUpdate()
    {
        _pendingPosition ??= _source.Dispatcher.InvokeAsync(() =>
        {
            _pendingPosition = null;
            UpdatePosition();
        }, DispatcherPriority.Normal);
    }

    private void UpdatePosition()
    {
        // The origin belongs to the primary display in the virtual desktop.
        var monitor = MonitorFromPoint(default, MonitorDefaultToPrimary);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        var dpi = GetDpiForWindow(_handle);
        if (monitor == 0 || dpi == 0 || !GetMonitorInfoW(monitor, ref info))
        {
            Trace.TraceWarning("Could not determine the primary display bounds or DPI.");
            FallBack();
            return;
        }

        var heightInPixels = (int)Math.Round(_height * dpi / 96);
        if (_registered)
        {
            var data = CreateData();
            data.Edge = AbeTop;
            data.Bounds = info.Monitor with { Bottom = info.Monitor.Top + heightInPixels };
            if (SHAppBarMessage(AbmQueryPos, ref data) != 0)
            {
                data.Bounds.Bottom = data.Bounds.Top + heightInPixels;
                if (ReserveBounds(ref data) && MoveTo(_approvedBounds, info.Monitor, dpi))
                {
                    return;
                }
            }

            Trace.TraceWarning("Could not position the Windows AppBar. Using the existing work area.");
            FallBack();
            if (!GetMonitorInfoW(monitor, ref info))
            {
                return;
            }
        }

        MoveTo(info.WorkArea with { Bottom = info.WorkArea.Top + heightInPixels }, info.Monitor, dpi);
    }

    private bool ReserveBounds(ref AppBarData data)
    {
        // ABM_SETPOS can generate another position notification.
        if (_requestedBounds == data.Bounds)
        {
            return true;
        }

        var requested = data.Bounds;
        if (SHAppBarMessage(AbmSetPos, ref data) == 0)
        {
            return false;
        }

        _requestedBounds = requested;
        _approvedBounds = data.Bounds;
        return true;
    }

    private void FallBack()
    {
        if (_registered)
        {
            Unregister();
            SetZOrder(HwndNotTopmost);
        }
    }

    private bool MoveTo(Rect bounds, Rect display, uint dpi)
    {
        var alreadyPositioned = GetWindowRect(_handle, out var current) && current == bounds;
        if (!alreadyPositioned && !SetWindowPos(_handle, 0, bounds.Left, bounds.Top,
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, SwpNoActivate | SwpNoZOrder))
        {
            Trace.TraceWarning("Could not move the top bar window.");
            return false;
        }

        // The shell can trim one side of the bar. Keep the hub aligned to the display midpoint.
        var offset = ((display.Left - bounds.Left) + (display.Right - bounds.Right)) * 48.0 / dpi;
        if (offset != _centerOffset)
        {
            _centerOffset = offset;
            CenterOffsetChanged?.Invoke(offset);
        }

        return true;
    }

    private void SetZOrder(nint after)
    {
        if (!SetWindowPos(_handle, after, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate))
        {
            Trace.TraceWarning("Could not change the AppBar window order.");
        }
    }

    private AppBarData CreateData() => new() { Size = (uint)Marshal.SizeOf<AppBarData>(), Window = _handle };

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint Size;
        public nint Window;
        public uint CallbackMessage;
        public uint Edge;
        public Rect Bounds;
        public nint Parameter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private record struct Rect(int Left, int Top, int Right, int Bottom);

    private readonly record struct WindowCandidate(nint Handle, bool Maximized, Rect Bounds);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int IsWindowArrangedCallback(nint hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern nuint SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetDesktopWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint FindWindowExW(nint parent, nint after, string? className, string? title);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool IsWindowEnabled(nint hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool IsZoomed(nint hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint GetModuleHandleW(string moduleName);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect value, int size);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int GetWindowLongW(nint hwnd, int index);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int SetWindowLongW(nint hwnd, int index, int value);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}

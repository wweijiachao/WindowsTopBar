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
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private static readonly nint HwndTopmost = -1;
    private static readonly nint HwndNotTopmost = -2;
    private static readonly nint HwndBottom = 1;

    private readonly HwndSource _source;
    private readonly nint _handle;
    private readonly double _height;
    private uint _callbackMessage;
    private DispatcherOperation? _pendingPosition;
    private Rect? _requestedBounds;
    private Rect _approvedBounds;
    private bool _registered;

    public AppBar(HwndSource source, double height)
    {
        _source = source;
        _handle = source.Handle;
        _height = height;
    }

    public void Start()
    {
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

    public void Dispose()
    {
        _source.RemoveHook(OnWindowMessage);
        _pendingPosition?.Abort();
        _pendingPosition = null;
        Unregister();
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
                if (ReserveBounds(ref data) && MoveTo(_approvedBounds))
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

        MoveTo(info.WorkArea with { Bottom = info.WorkArea.Top + heightInPixels });
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

    private bool MoveTo(Rect bounds)
    {
        if (GetWindowRect(_handle, out var current)
            && current.Left == bounds.Left && current.Top == bounds.Top
            && current.Right == bounds.Right && current.Bottom == bounds.Bottom)
        {
            return true;
        }

        if (SetWindowPos(_handle, 0, bounds.Left, bounds.Top,
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, SwpNoActivate | SwpNoZOrder))
        {
            return true;
        }

        Trace.TraceWarning("Could not move the top bar window.");
        return false;
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}

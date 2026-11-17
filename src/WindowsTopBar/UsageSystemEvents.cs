using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowsTopBar;

internal sealed class UsageSystemEvents : IDisposable
{
    public const int WmPowerBroadcast = 0x0218;
    public const int PbtApmResumeAutomatic = 0x0012;
    private const int WmSessionChange = 0x02B1;
    private const int SessionLock = 7;
    private const int SessionUnlock = 8;
    private const int Suspend = 4;
    private const int PowerSettingChange = 0x8013;
    private static readonly Guid SessionDisplayStatus = new("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");
    private nint _sessionWindow;
    private nint _displayRegistration;

    public UsageSystemEvents(nint window)
    {
        if (WTSRegisterSessionNotification(window, 0)) // NOTIFY_FOR_THIS_SESSION
        {
            _sessionWindow = window;
        }
        else
        {
            Trace.TraceWarning("Could not register for Windows session lock notifications.");
        }

        _displayRegistration = RegisterPowerSettingNotification(window, in SessionDisplayStatus, 0); // DEVICE_NOTIFY_WINDOW_HANDLE
        if (_displayRegistration == 0)
        {
            Trace.TraceWarning("Could not register for Windows display power notifications.");
        }
    }

    public static long AwakeMilliseconds
    {
        get
        {
            return QueryUnbiasedInterruptTime(out var units) ? (long)(units / 10_000) : Environment.TickCount64;
        }
    }

    public static long? ReadLastInputMilliseconds()
    {
        var input = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref input))
        {
            return null;
        }

        // The Windows input timestamp wraps at 32 bits, unlike TickCount64 on .NET 10.
        var now = Environment.TickCount64;
        return now - unchecked((uint)now - input.TickCount);
    }

    public static (UsageInterruption Reason, bool Interrupted)? ReadInterruption(int message, nint kind, nint data)
    {
        if (message == WmSessionChange && kind is SessionLock or SessionUnlock)
        {
            return (UsageInterruption.Locked, kind == SessionLock);
        }
        if (message != WmPowerBroadcast)
        {
            return null;
        }
        if (kind is Suspend or PbtApmResumeAutomatic)
        {
            return (UsageInterruption.Suspended, kind == Suspend);
        }
        if (kind != PowerSettingChange || data == 0)
        {
            return null;
        }

        var setting = Marshal.PtrToStructure<PowerSettingHeader>(data);
        if (setting.Id != SessionDisplayStatus || setting.DataLength != sizeof(int))
        {
            return null;
        }

        var state = Marshal.ReadInt32(data, Marshal.SizeOf<PowerSettingHeader>());
        return state is 0 or 1 or 2 ? (UsageInterruption.DisplayOff, state == 0) : null;
    }

    public void Dispose()
    {
        if (_sessionWindow != 0 && !WTSUnRegisterSessionNotification(_sessionWindow))
        {
            Trace.TraceWarning("Could not unregister Windows session lock notifications.");
        }
        if (_displayRegistration != 0 && !UnregisterPowerSettingNotification(_displayRegistration))
        {
            Trace.TraceWarning("Could not unregister Windows display power notifications.");
        }

        _sessionWindow = 0;
        _displayRegistration = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size, TickCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerSettingHeader
    {
        public Guid Id;
        public uint DataLength;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern bool QueryUnbiasedInterruptTime(out ulong time);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("wtsapi32.dll", ExactSpelling = true)]
    private static extern bool WTSRegisterSessionNotification(nint window, uint flags);

    [DllImport("wtsapi32.dll", ExactSpelling = true)]
    private static extern bool WTSUnRegisterSessionNotification(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint RegisterPowerSettingNotification(nint window, in Guid setting, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool UnregisterPowerSettingNotification(nint registration);
}

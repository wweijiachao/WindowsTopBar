using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowsTopBar;

internal enum Messenger { QQ, WeChat }

internal sealed class MessengerWindows : IDisposable
{
    private const int ShellFlash = 0x8006;
    private static readonly string[] WeChatProcesses = ["Weixin", "WeChat"];
    private nint _window;
    private uint _message;

    public void Register(nint window)
    {
        var message = RegisterWindowMessageW("SHELLHOOK");
        if (message != 0 && RegisterShellHookWindow(window))
        {
            _window = window;
            _message = message;
        }
        else
        {
            Trace.TraceWarning("Could not register for messenger attention events.");
        }
    }

    public Messenger? ReadFlash(int message, nint kind, nint target)
    {
        if (_message == 0 || message != _message || kind != ShellFlash ||
            GetWindowThreadProcessId(target, out var pid) == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)pid);
            var name = process.ProcessName;
            if (name.Equals("QQ", StringComparison.OrdinalIgnoreCase))
            {
                return Messenger.QQ;
            }

            return WeChatProcesses.Contains(name, StringComparer.OrdinalIgnoreCase) ? Messenger.WeChat : null;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public static (bool QQ, bool WeChat) ReadRunning() =>
        (FindProcess("QQ", requireWindow: false) != 0,
            WeChatProcesses.Any(name => FindProcess(name, requireWindow: false) != 0));

    public static void Activate(Messenger messenger)
    {
        var names = messenger == Messenger.QQ ? ["QQ"] : WeChatProcesses;
        foreach (var name in names)
        {
            var window = FindProcess(name, requireWindow: true);
            if (window == 0)
            {
                continue;
            }

            if (IsIconic(window))
            {
                ShowWindowAsync(window, 9); // SW_RESTORE
            }

            SetForegroundWindow(window);
            return;
        }
    }

    private static nint FindProcess(string name, bool requireWindow)
    {
        using var current = Process.GetCurrentProcess();
        var processes = Process.GetProcessesByName(name);
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.SessionId == current.SessionId && !process.HasExited)
                    {
                        var match = requireWindow ? process.MainWindowHandle : 1;
                        if (match != 0)
                        {
                            return match;
                        }
                    }
                }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // A process can exit while it is being enumerated.
                }
            }

            return 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_window != 0)
        {
            DeregisterShellHookWindow(_window);
            _window = 0;
            _message = 0;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool RegisterShellHookWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool DeregisterShellHookWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool ShowWindowAsync(nint window, int command);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool SetForegroundWindow(nint window);
}

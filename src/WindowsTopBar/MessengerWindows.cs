using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowsTopBar;

internal enum Messenger { QQ, WeChat }

internal sealed class MessengerWindows : IDisposable
{
    private const int ShellFlash = 0x8006;
    private const int ShellWindowActivated = 4;
    private const int ShellRudeAppActivated = 0x8004;
    private const uint WmClose = 0x0010;
    private static readonly string[] WeChatProcesses = ["Weixin", "WeChat"];
    private nint _window;
    private uint _message;
    private nint _lastExternalForeground;

    public void Register(nint window)
    {
        var message = RegisterWindowMessageW("SHELLHOOK");
        if (message != 0 && RegisterShellHookWindow(window))
        {
            _window = window;
            _message = message;
            var foreground = GetForegroundWindow();
            if (foreground != window)
            {
                _lastExternalForeground = foreground;
            }
        }
        else
        {
            Trace.TraceWarning("Could not register for messenger attention events.");
        }
    }

    public Messenger? ReadFlash(int message, nint kind, nint target)
    {
        if (_message == 0 || message != _message || target == 0)
        {
            return null;
        }

        if (kind is ShellWindowActivated or ShellRudeAppActivated)
        {
            if (target != _window)
            {
                _lastExternalForeground = target;
            }

            return null;
        }

        if (kind != ShellFlash || GetWindowThreadProcessId(target, out var pid) == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)pid);
            return GetMessenger(process.ProcessName);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public bool TryMinimizeToTray(Messenger messenger)
    {
        var foreground = GetForegroundWindow();
        // A WPF click can give the top bar focus before its Click handler runs.
        var target = foreground == 0 || foreground == _window ? _lastExternalForeground : foreground;
        if (target == 0 || !IsWindowVisible(target) || IsIconic(target) ||
            GetWindowThreadProcessId(target, out var pid) == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)pid);
            if (GetMessenger(process.ProcessName) != messenger || process.MainWindowHandle != target)
            {
                return false;
            }

            // Use the client's own close-to-tray behavior so the native tray icon can restore it.
            return PostMessageW(target, WmClose, 0, 0);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static Messenger? GetMessenger(string name)
    {
        if (name.Equals("QQ", StringComparison.OrdinalIgnoreCase))
        {
            return Messenger.QQ;
        }

        return WeChatProcesses.Contains(name, StringComparer.OrdinalIgnoreCase) ? Messenger.WeChat : null;
    }

    public static (bool QQ, bool WeChat) ReadRunning() =>
        (FindProcess("QQ", requireWindow: false) != 0,
            WeChatProcesses.Any(name => FindProcess(name, requireWindow: false) != 0));

    public static async Task Activate(Messenger messenger, CancellationToken cancellationToken)
    {
        if (await SystemShortcuts.InvokeMessengerTrayIcon(messenger, cancellationToken).ConfigureAwait(false) ||
            cancellationToken.IsCancellationRequested)
        {
            return;
        }

        string[] names = messenger == Messenger.QQ ? ["QQ"] : WeChatProcesses;
        foreach (var name in names)
        {
            var window = FindProcess(name, requireWindow: true);
            if (window == 0 || !IsWindowVisible(window) || !IsWindowEnabled(window))
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
            _lastExternalForeground = 0;
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
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool ShowWindowAsync(nint window, int command);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool IsWindowEnabled(nint window);
}

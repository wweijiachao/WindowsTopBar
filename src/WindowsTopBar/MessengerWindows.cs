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
    private static readonly string[] QqProcesses = ["QQ"];
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

    public (Messenger? Flash, Messenger? Activated) ReadShellEvent(int message, nint kind, nint target, bool watchActivation)
    {
        if (_message == 0 || message != _message || target == 0)
        {
            return default;
        }

        if (kind is ShellWindowActivated or ShellRudeAppActivated)
        {
            if (target != _window)
            {
                _lastExternalForeground = target;
            }

            return (null, watchActivation && target == GetForegroundWindow()
                ? ReadMessenger(target, requireMainWindow: true)
                : null);
        }

        return (kind == ShellFlash ? ReadMessenger(target, requireMainWindow: false) : null, null);
    }

    public static Messenger? ReadForegroundMessenger() => ReadMessenger(GetForegroundWindow(), requireMainWindow: true);

    private static Messenger? ReadMessenger(nint target, bool requireMainWindow)
    {
        if (target == 0 || GetWindowThreadProcessId(target, out var pid) == 0 ||
            (requireMainWindow && (!IsWindowVisible(target) || IsIconic(target))))
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)pid);
            var messenger = GetMessenger(process.ProcessName);
            return messenger.HasValue && (!requireMainWindow || process.MainWindowHandle == target) ? messenger : null;
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
        if (ReadMessenger(target, requireMainWindow: true) != messenger)
        {
            return false;
        }

        // Use the client's own close-to-tray behavior so the native tray icon can restore it.
        return PostMessageW(target, WmClose, 0, 0);
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
        (IsRunning(QqProcesses), IsRunning(WeChatProcesses));

    private static bool IsRunning(string[] names) =>
        names.Any(name => FindProcess(name, requireWindow: false) != 0);

    public static void Activate(Messenger messenger, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var names = messenger == Messenger.QQ ? QqProcesses : WeChatProcesses;
        // A tray-hidden window needs the client's own restore handler.
        if (!TryActivateMainWindow(names) && IsRunning(names) && !cancellationToken.IsCancellationRequested)
        {
            var settings = AppearanceSettings.Load(AppearanceSettings.FilePath);
            if (!cancellationToken.IsCancellationRequested)
            {
                SystemShortcuts.SendMessengerShortcut(messenger == Messenger.QQ
                    ? settings.QqOpenShortcut
                    : settings.WeChatOpenShortcut);
            }
        }
    }

    private static bool TryActivateMainWindow(string[] names)
    {
        foreach (var name in names)
        {
            var window = FindProcess(name, requireWindow: true);
            if (window == 0 || !IsWindowVisible(window) || !IsWindowEnabled(window))
            {
                continue;
            }

            // Asynchronous restore can finish after the foreground request.
            var restoreRequested = IsIconic(window) && ShowWindowAsync(window, 9); // SW_RESTORE
            if (SetForegroundWindow(window) || restoreRequested)
            {
                return true;
            }
        }

        return false;
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

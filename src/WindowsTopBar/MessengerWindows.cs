using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowsTopBar;

internal enum Messenger { QQ, WeChat }

internal sealed class MessengerWindows : IDisposable
{
    private const int ShellFlash = 0x8006;
    private const int ShellWindowActivated = 4;
    private const int ShellRudeAppActivated = 0x8004;
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0;
    private const uint WinEventSkipOwnProcess = 2;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint WmClose = 0x0010;
    private static readonly string[] QqProcesses = ["QQ"];
    private static readonly string[] WeChatProcesses = ["Weixin", "WeChat"];
    private nint _window;
    private uint _message;
    private nint _lastExternalForeground;
    private readonly WinEventProc _foregroundCallback;
    private nint _foregroundHook;

    public MessengerWindows() => _foregroundCallback = OnForegroundChanged;

    public event Action<nint>? ForegroundWindowChanged;

    public void Register(nint window)
    {
        _window = window;
        var foreground = GetForegroundWindow();
        if (IsExternalWindow(foreground))
        {
            _lastExternalForeground = foreground;
        }

        var message = RegisterWindowMessageW("SHELLHOOK");
        if (message != 0 && RegisterShellHookWindow(window))
        {
            _message = message;
        }
        else
        {
            Trace.TraceWarning("Could not register for messenger attention events.");
        }

        // Out-of-context callbacks are delivered on the UI thread that registers the hook.
        _foregroundHook = SetWinEventHook(EventSystemForeground, EventSystemForeground, 0,
            _foregroundCallback, 0, 0, WinEventOutOfContext | WinEventSkipOwnProcess);
        if (_foregroundHook == 0)
        {
            Trace.TraceWarning("Could not register for foreground-window events.");
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
            if (IsExternalWindow(target))
            {
                _lastExternalForeground = target;
                // Keep the shell notification as a backup when it agrees with the foreground.
                if (target == GetForegroundWindow())
                {
                    ForegroundWindowChanged?.Invoke(target);
                }
            }
        }

        return kind == ShellFlash ? ReadMessenger(target, requireMainWindow: false) : null;
    }

    private void OnForegroundChanged(nint hook, uint eventType, nint window, int objectId, int childId, uint eventThread, uint eventTime)
    {
        if (hook == _foregroundHook && IsExternalWindow(window))
        {
            _lastExternalForeground = window;
            ForegroundWindowChanged?.Invoke(window);
        }
    }

    public nint InteractionForegroundWindow
    {
        get
        {
            var foreground = GetForegroundWindow();
            return ResolveInteractionForeground(foreground, !IsExternalWindow(foreground), _lastExternalForeground);
        }
    }

    private static bool IsExternalWindow(nint window) =>
        GetWindowThreadProcessId(window, out var pid) != 0 && pid != Environment.ProcessId;

    internal static nint ResolveInteractionForeground(nint foreground, bool ownProcess, nint lastExternal) =>
        foreground == 0 || ownProcess ? lastExternal : foreground;

    public static Messenger? ReadForegroundMessenger() => ReadAttentionWindow(GetForegroundWindow());

    public static Messenger? ReadAttentionWindow(nint window)
    {
        if (window == 0 || !IsWindowVisible(window) || IsIconic(window))
        {
            return null;
        }

        // The active client window can differ from Process.MainWindowHandle.
        var style = GetWindowLongW(window, GwlExStyle);
        if ((style == 0 && Marshal.GetLastPInvokeError() != 0) || (style & (WsExToolWindow | WsExNoActivate)) != 0)
        {
            return null;
        }

        return ReadMessenger(window, requireMainWindow: false);
    }

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
        // A WPF click can give the top bar focus before its Click handler runs.
        var target = InteractionForegroundWindow;
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
                SystemShortcuts.TrySendConfiguredShortcut(messenger == Messenger.QQ
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
        if (_foregroundHook != 0)
        {
            if (!UnhookWinEvent(_foregroundHook))
            {
                Trace.TraceWarning("Could not remove the foreground-window hook.");
            }
            _foregroundHook = 0;
        }

        if (_message != 0 && !DeregisterShellHookWindow(_window))
        {
            Trace.TraceWarning("Could not remove the messenger shell hook.");
        }
        _window = 0;
        _message = 0;
        _lastExternalForeground = 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void WinEventProc(nint hook, uint eventType, nint window, int objectId, int childId,
        uint eventThread, uint eventTime);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc callback,
        uint processId, uint threadId, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int GetWindowLongW(nint window, int index);

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

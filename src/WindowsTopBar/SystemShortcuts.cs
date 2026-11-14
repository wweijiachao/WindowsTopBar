using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace WindowsTopBar;

internal static class SystemShortcuts
{
    private const ushort LeftWindows = 0x5B;
    private const uint KeyUp = 2;

    public static async Task OpenQuickSettings(int right, int top, int gap, CancellationToken cancellationToken)
    {
        if (IsQuickSettings(GetForegroundWindow()))
        {
            SendWindowsShortcut(0x41);
            return;
        }

        if (!InvokeTaskbar(FindQuickSettingsButton))
        {
            SendWindowsShortcut(0x41);
        }

        try
        {
            // The visible content slides into place inside the shell host.
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            for (int attempt = 0; attempt < 6; attempt++)
            {
                await Task.Delay(90, cancellationToken).ConfigureAwait(false);
                var panel = GetForegroundWindow();
                if (IsQuickSettings(panel) && TryPositionPanel(panel, right, top, gap))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static AutomationElement? FindQuickSettingsButton() => GetTaskbar()?.FindFirst(TreeScope.Descendants,
        new AndCondition(
            new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon"),
            new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.OmniButtonCenter")));

    private static AutomationElement? GetTaskbar()
    {
        var taskbar = FindWindowW("Shell_TrayWnd", null);
        return taskbar == 0 ? null : AutomationElement.FromHandle(taskbar);
    }

    private static bool InvokeTaskbar(Func<AutomationElement?> findButton)
    {
        try
        {
            return Invoke(findButton());
        }
        catch (Exception error) when (IsAutomationFailure(error))
        {
            Trace.TraceWarning("Could not invoke a taskbar button: {0}", error.Message);
        }

        return false;
    }

    private static bool Invoke(AutomationElement? button)
    {
        if (button?.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) != true)
        {
            return false;
        }

        ((InvokePattern)pattern).Invoke();
        return true;
    }

    private static bool TryPositionPanel(nint window, int right, int top, int gap)
    {
        try
        {
            // The shell host can span the whole work area. Anchor the visible content inside it.
            var region = AutomationElement.FocusedElement;
            while (region is not null && region.Current.AutomationId != "ControlCenterRegion")
            {
                region = TreeWalker.RawViewWalker.GetParent(region);
            }
            var bounds = region?.Current.BoundingRectangle ?? Rect.Empty;
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 ||
                !GetWindowRect(window, out var host) || GetForegroundWindow() != window)
            {
                return false;
            }

            const uint noResizeNoZOrderNoActivate = 0x0015;
            return SetWindowPos(window, 0, host.Left + right - gap - (int)bounds.Right,
                host.Top + top + gap - (int)bounds.Top, 0, 0, noResizeNoZOrderNoActivate);
        }
        catch (Exception error) when (IsAutomationFailure(error))
        {
            return false;
        }
    }

    private static bool IsAutomationFailure(Exception error) =>
        error is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException;

    private static bool IsQuickSettings(nint window)
    {
        var name = new System.Text.StringBuilder(32);
        return window != 0 && GetClassNameW(window, name, name.Capacity) != 0 &&
            name.ToString() == "ControlCenterWindow";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left, Top, Right, Bottom;
    }

    private static void SendWindowsShortcut(ushort key)
    {
        if (!TrySendShortcut(LeftWindows, key))
        {
            Trace.TraceWarning("Could not send the Windows keyboard shortcut.");
        }
    }

    public static void SendCtrlAltShortcut(ushort key)
    {
        const ushort shift = 0x10;
        const ushort control = 0x11;
        const ushort alt = 0x12;
        const ushort rightWindows = 0x5C;
        if (GetAsyncKeyState(shift) < 0 || GetAsyncKeyState(control) < 0 || GetAsyncKeyState(alt) < 0 ||
            GetAsyncKeyState(LeftWindows) < 0 || GetAsyncKeyState(rightWindows) < 0 || GetAsyncKeyState(key) < 0)
        {
            return;
        }

        if (!TrySendShortcut(control, alt, key))
        {
            Trace.TraceWarning("Could not send Ctrl+Alt+{0}.", (char)key);
        }
    }

    private static bool TrySendShortcut(params ushort[] keys)
    {
        var inputs = new Input[keys.Length * 2];
        for (int index = 0; index < keys.Length; index++)
        {
            inputs[index] = Key(keys[index], 0);
            inputs[inputs.Length - 1 - index] = Key(keys[index], KeyUp);
        }

        var size = Marshal.SizeOf<Input>();
        var sent = (int)SendInput((uint)inputs.Length, inputs, size);
        if (sent == inputs.Length)
        {
            return true;
        }

        // The tail of the sequence releases any keys still down after a partial send.
        var held = Math.Min(sent, inputs.Length - sent);
        if (held > 0 && SendInput((uint)held, inputs[^held..], size) != held)
        {
            Trace.TraceWarning("Could not release all keys after sending a keyboard shortcut.");
        }

        return false;
    }

    private static Input Key(ushort key, uint flags) => new()
    {
        Type = 1,
        Data = new InputData { Keyboard = new KeyboardInput { Key = key, Flags = flags } }
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputData Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Key, Scan;
        public uint Flags, Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint SendInput(uint count, [In] Input[] inputs, int size);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint FindWindowW(string className, string? windowName);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetClassNameW(nint window, System.Text.StringBuilder className, int size);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetWindowRect(nint window, out WindowRect bounds);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool SetWindowPos(nint window, nint order, int x, int y, int width, int height, uint flags);
}

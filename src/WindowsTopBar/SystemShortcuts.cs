using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace WindowsTopBar;

internal static class SystemShortcuts
{
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

    public static async Task<bool> InvokeMessengerTrayIcon(Messenger messenger, CancellationToken cancellationToken)
    {
        AutomationElement? overflowButton = null;
        bool openedOverflow = false;
        try
        {
            if (cancellationToken.IsCancellationRequested || GetTaskbar() is not { } taskbar)
            {
                return false;
            }

            if (Invoke(FindMessengerTrayIcon(taskbar, messenger)))
            {
                return true;
            }

            if (GetTrayOverflow() is { } existingOverflow)
            {
                return Invoke(FindMessengerTrayIcon(existingOverflow, messenger));
            }

            overflowButton = taskbar.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon"),
                new OrCondition(
                    new PropertyCondition(AutomationElement.NameProperty, "显示隐藏的图标"),
                    new PropertyCondition(AutomationElement.NameProperty, "Show hidden icons"))));
            openedOverflow = Invoke(overflowButton);
            if (!openedOverflow)
            {
                return false;
            }

            for (int attempt = 0; attempt < 5; attempt++)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                if (GetTrayOverflow() is { } overflow && Invoke(FindMessengerTrayIcon(overflow, messenger)))
                {
                    return true;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error) when (IsAutomationFailure(error))
        {
            Trace.TraceWarning("Could not invoke the messenger's tray icon: {0}", error.Message);
        }
        finally
        {
            if (openedOverflow)
            {
                try
                {
                    if (GetTrayOverflow() is not null)
                    {
                        Invoke(overflowButton);
                    }
                }
                catch (Exception error) when (IsAutomationFailure(error))
                {
                    Trace.TraceWarning("Could not close the tray overflow: {0}", error.Message);
                }
            }
        }

        return false;
    }

    private static AutomationElement? GetTrayOverflow()
    {
        var window = FindWindowW("TopLevelWindowForOverflowXamlIsland", null);
        if (window == 0 || !IsWindowVisible(window))
        {
            window = FindWindowW("NotifyIconOverflowWindow", null);
        }

        return window != 0 && IsWindowVisible(window) ? AutomationElement.FromHandle(window) : null;
    }

    private static AutomationElement? FindMessengerTrayIcon(AutomationElement tray, Messenger messenger)
    {
        var icons = tray.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "NotifyItemIcon"));
        foreach (AutomationElement icon in icons)
        {
            var name = icon.Current.Name.AsSpan().TrimStart();
            var separator = name.IndexOfAny(":：\r\n");
            var label = (separator < 0 ? name : name[..separator]).TrimEnd();
            var matches = messenger == Messenger.QQ
                ? label.Equals("QQ", StringComparison.OrdinalIgnoreCase)
                : label.Equals("微信", StringComparison.Ordinal) ||
                    label.Equals("Weixin", StringComparison.OrdinalIgnoreCase) ||
                    label.Equals("WeChat", StringComparison.OrdinalIgnoreCase);
            if (matches && !icon.Current.IsOffscreen)
            {
                return icon;
            }
        }

        return null;
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
        const ushort leftWindows = 0x5B;
        const uint keyUp = 2;
        Input[] keys = [Key(leftWindows, 0), Key(key, 0), Key(key, keyUp), Key(leftWindows, keyUp)];
        var size = Marshal.SizeOf<Input>();
        if (SendInput((uint)keys.Length, keys, size) != keys.Length)
        {
            Trace.TraceWarning("Could not send the Windows keyboard shortcut.");
        }
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint FindWindowW(string className, string? windowName);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetClassNameW(nint window, System.Text.StringBuilder className, int size);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetWindowRect(nint window, out WindowRect bounds);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool SetWindowPos(nint window, nint order, int x, int y, int width, int height, uint flags);
}

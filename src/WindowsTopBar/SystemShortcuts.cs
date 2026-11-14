using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace WindowsTopBar;

internal static class SystemShortcuts
{
    public static void OpenQuickSettings()
    {
        try
        {
            var taskbar = FindWindowW("Shell_TrayWnd", null);
            if (taskbar != 0)
            {
                var button = AutomationElement.FromHandle(taskbar).FindFirst(TreeScope.Descendants,
                    new AndCondition(
                        new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon"),
                        new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.OmniButtonCenter")));
                if (button?.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) == true)
                {
                    ((InvokePattern)pattern).Invoke();
                    return;
                }
            }
        }
        catch (Exception error) when (error is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            Trace.TraceWarning("Could not invoke the taskbar's Quick Settings button: {0}", error.Message);
        }

        SendWindowsShortcut(0x41);
    }

    public static void SwitchInputLanguage() => SendWindowsShortcut(0x20);

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
}

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WindowsTopBar;

internal sealed record ShortcutWindow(nint Handle, int ProcessId);
internal enum ShortcutActivation { Activated, Minimized, Missing, Blocked }

internal static class ShortcutWindows
{
    public static Dictionary<int, string> ReadProcesses(IEnumerable<string> executables)
    {
        var paths = executables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<int, string>();
        if (paths.Count == 0) return result;
        using var current = Process.GetCurrentProcess();
        foreach (var name in paths.Select(Path.GetFileNameWithoutExtension).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(name); }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException) { continue; }
            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        if (process.SessionId != current.SessionId || process.HasExited) continue;
                        using var handle = OpenProcess(0x1000, false, process.Id); // QUERY_LIMITED_INFORMATION; no VM_READ
                        if (handle.IsInvalid) continue;
                        var buffer = new StringBuilder(32768);
                        var capacity = buffer.Capacity;
                        if (QueryFullProcessImageNameW(handle, 0, buffer, ref capacity) && paths.TryGetValue(buffer.ToString(), out var path))
                            result[process.Id] = path;
                    }
                    catch (Exception error) when (error is Win32Exception or InvalidOperationException or ArgumentException)
                    {
                        // An exiting or inaccessible process contributes no positive running signal.
                    }
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return result;
    }

    public static ShortcutWindow[] ReadWindows()
    {
        var windows = new List<ShortcutWindow>();
        if (!EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || !IsWindowEnabled(window)) return true;
            var style = GetWindowLongW(window, -20);
            if ((style == 0 && Marshal.GetLastPInvokeError() != 0) || (style & (0x80 | 0x08000000)) != 0) return true;
            if (DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int)) < 0 || cloaked != 0) return true;
            if (GetWindowThreadProcessId(window, out var pid) != 0)
                windows.Add(new(window, (int)pid));
            return true;
        }, 0)) return [];
        return windows.ToArray();
    }

    public static async Task<ShortcutActivation> ToggleAsync(nint window, nint foreground, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsWindow(window)) return ShortcutActivation.Missing;
        if (window == foreground)
            return ShowWindowAsync(window, 6) ? ShortcutActivation.Minimized : ShortcutActivation.Blocked; // SW_MINIMIZE
        if (IsIconic(window) && !ShowWindowAsync(window, 9)) return ShortcutActivation.Blocked; // SW_RESTORE
        for (var attempt = 0; attempt < 4; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (!IsWindow(window)) return ShortcutActivation.Missing;
            // Verify the actual foreground: a successfully queued restore alone is not activation.
            var requested = SetForegroundWindow(window);
            if (GetForegroundWindow() == window && !IsIconic(window)) return ShortcutActivation.Activated;
            if (!requested && !IsIconic(window)) return ShortcutActivation.Blocked;
            await Task.Delay(50, token);
        }
        return ShortcutActivation.Blocked;
    }

    private delegate bool EnumWindowsProc(nint window, nint data);
    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint data);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder path, ref int capacity);
    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int GetWindowLongW(nint window, int index);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", ExactSpelling = true)] private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern bool SetForegroundWindow(nint window);
}

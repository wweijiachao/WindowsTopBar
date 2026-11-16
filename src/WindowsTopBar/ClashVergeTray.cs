using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using Microsoft.Win32.SafeHandles;

namespace WindowsTopBar;

internal static class ClashVergeTray
{
    private const string TrayWindowClass = "tray_icon_app";
    private const uint QueryLimitedInformation = 0x1000;
    // Clash Verge Rev 2.5.10 uses tray-icon 0.25.1 on Windows.
    private const uint TrayIconCallback = 6002;
    private const int RightButtonUp = 0x0205;

    public static bool IsRunning() => FindTrayWindow() is not null;

    public static string? ReadTooltip()
    {
        var tooltip = ReadShellTooltip();
        if (tooltip is not null)
        {
            return tooltip;
        }

        if (FindTrayWindow() is not { } tray)
        {
            return null;
        }

        var version = ReadVersion(tray.Executable)?.FileVersion;
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "io.github.clash-verge-rev.clash-verge-rev");
        return ClashVergeSettings.ReadTooltip(folder, version);
    }

    private static string? ReadShellTooltip()
    {
        try
        {
            return ReadTooltip(SystemShortcuts.FindShellWindow("Shell_TrayWnd")) ??
                ReadTooltip(SystemShortcuts.FindShellWindow("TopLevelWindowForOverflowXamlIsland"));
        }
        catch (Exception error) when (SystemShortcuts.IsAutomationFailure(error))
        {
            return null;
        }
    }

    private static string? ReadTooltip(AutomationElement? container)
    {
        if (container is null)
        {
            return null;
        }

        var cache = new CacheRequest { AutomationElementMode = AutomationElementMode.None };
        cache.Add(AutomationElement.NameProperty);
        cache.Add(AutomationElement.HelpTextProperty);
        using var request = cache.Activate();
        var buttons = container.FindAll(TreeScope.Descendants, new OrCondition(
            new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon"),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)));

        foreach (AutomationElement button in buttons)
        {
            var name = button.Cached.Name?.Trim() ?? string.Empty;
            var help = button.Cached.HelpText?.Trim() ?? string.Empty;
            if (IsTooltip(help))
            {
                return IsTooltip(name) && name.Length > help.Length ? name : help;
            }
            if (IsTooltip(name))
            {
                return name;
            }
        }

        return null;
    }

    private static bool IsTooltip(string text) =>
        text.StartsWith("Clash Verge ", StringComparison.OrdinalIgnoreCase) &&
        text.Count(character => character == '\n') >= 3;

    public static (nint Window, uint ProcessId)? FindMenuTarget()
    {
        if (FindTrayWindow() is not { } tray)
        {
            return null;
        }

        var version = ReadVersion(tray.Executable);
        return version is { FileMajorPart: 2, FileMinorPart: 5, FileBuildPart: 10 }
            ? (tray.Window, tray.ProcessId) : null;
    }

    public static bool TryShowMenu((nint Window, uint ProcessId)? target) =>
        target is { } tray && GetWindowThreadProcessId(tray.Window, out var owner) != 0 && owner == tray.ProcessId &&
        PostMessageW(tray.Window, TrayIconCallback, 0, RightButtonUp);

    private static FileVersionInfo? ReadVersion(string executable)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(executable);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (nint Window, uint ProcessId, string Executable)? FindTrayWindow()
    {
        for (var window = FindWindowExW(0, 0, TrayWindowClass, null);
            window != 0; window = FindWindowExW(0, window, TrayWindowClass, null))
        {
            if (GetWindowThreadProcessId(window, out var processId) == 0)
            {
                continue;
            }

            using var process = OpenProcess(QueryLimitedInformation, false, processId);
            if (process.IsInvalid)
            {
                continue;
            }

            var path = new StringBuilder(1024);
            var size = (uint)path.Capacity;
            if (!QueryFullProcessImageNameW(process, 0, path, ref size))
            {
                continue;
            }

            var executable = path.ToString();
            var fileName = Path.GetFileName(executable);
            if (fileName.Equals("clash-verge.exe", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("Clash Verge.exe", StringComparison.OrdinalIgnoreCase))
            {
                return (window, processId, executable);
            }
        }

        return null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint FindWindowExW(nint parent, nint after, string className, string? title);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder name, ref uint size);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
}

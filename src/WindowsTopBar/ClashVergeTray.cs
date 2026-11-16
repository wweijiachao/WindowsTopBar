using System.ComponentModel;
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

    public static bool IsRunning() => FindRunningExecutable() is not null;

    public static string? ReadTooltip()
    {
        var tooltip = ReadShellTooltip();
        if (tooltip is not null)
        {
            return tooltip;
        }

        if (FindRunningExecutable() is not { } executable)
        {
            return null;
        }

        var version = ReadVersion(executable)?.FileVersion;
        return ClashVergeSettings.ReadTooltip(ClashVergeSettings.Folder, version);
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

    public static string? RunCommand(string command, CancellationToken cancellationToken)
    {
        var executable = FindRunningExecutable();
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        if (executable is null)
        {
            return "Clash Verge 未运行。";
        }

        if (command == "dashboard")
        {
            try
            {
                // Starting the executable again asks its existing instance to show the dashboard.
                ShortcutDirectory.Launch(executable);
                return null;
            }
            catch (Win32Exception)
            {
                return "无法打开 Clash Verge 仪表板。";
            }
        }

        var shortcut = ClashVergeSettings.ReadGlobalShortcut(ClashVergeSettings.Folder, command);
        if (shortcut is null)
        {
            return command == "quit"
                ? "请在 Clash Verge 的 verge.yaml 中将「- quit,CTRL+ALT+F11」加到 hotkeys 列表，开启全局快捷键后重启 Clash Verge。"
                : "请在 Clash Verge 的快捷键设置中为「开启/关闭 TUN 模式」设置按键，并开启全局快捷键。";
        }

        // Clash Verge only handles a Q-based quit shortcut while its own window has focus.
        if (command == "quit" && shortcut[(shortcut.LastIndexOf('+') + 1)..].Trim().Equals("Q", StringComparison.OrdinalIgnoreCase))
        {
            return "请把 Clash Verge 的退出快捷键改用其他主键，例如 Ctrl+Alt+F11。";
        }

        return cancellationToken.IsCancellationRequested || SystemShortcuts.TrySendConfiguredShortcut(shortcut)
            ? null : "无法发送 Clash Verge 快捷键，请检查按键配置后重试。";
    }

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

    private static string? FindRunningExecutable()
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
                return executable;
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
}

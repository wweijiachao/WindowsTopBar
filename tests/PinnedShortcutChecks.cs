#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property UseAppHost=false
#:property Nullable=enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

internal static class PinnedShortcutChecks
{
    [STAThread]
    public static void Main(string[] args)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        Type Type(string name) => assembly.GetType("WindowsTopBar." + name, true)!;
        var catalog = (IDisposable)Activator.CreateInstance(Type("PinnedShortcuts"))!;
        var root = Directory.CreateTempSubdirectory("topbar-shortcuts-");
        object? shell = null;
        var bar = (Window)Activator.CreateInstance(Type("MainWindow"))!;
        try
        {
            object? Read(bool force = true) => Result((Task)Type("PinnedShortcuts").GetMethod("ReadAsync")!
                .Invoke(catalog, [force, root.FullName])!);
            Array Entries(object snapshot, string name) => (Array)snapshot.GetType().GetProperty(name)!.GetValue(snapshot)!;
            var pins = Path.Combine(root.FullName, "Pinned");
            Read();
            Check(Directory.Exists(pins), "Pinned was not created.");
            File.WriteAllText(Path.Combine(root.FullName, "root_name.url"), "");
            File.WriteAllText(Path.Combine(pins, "zebra.url"), "");
            File.WriteAllText(Path.Combine(pins, "alpha.url"), "");
            Directory.CreateDirectory(Path.Combine(pins, "Nested"));
            File.WriteAllText(Path.Combine(pins, "Nested", "hidden.lnk"), "");
            var snapshot = Read()!;
            Check(Entries(snapshot, "Menu").Length == 1 && Entries(snapshot, "Pinned").Length == 2, "Directory grouping/recursion failed.");
            Check((string)Type("ShortcutEntry").GetProperty("Name")!.GetValue(Entries(snapshot, "Pinned").GetValue(0))! == "alpha", "Filename ordering failed.");
            File.Move(Path.Combine(pins, "alpha.url"), Path.Combine(root.FullName, "alpha.url"));
            PumpUntil(() => Read(false) is { } update && Entries(update, "Pinned").Length == 1);
            File.Move(Path.Combine(pins, "zebra.url"), Path.Combine(pins, "微信.url"));
            PumpUntil(() => Read(false) is { } update && (string)Type("ShortcutEntry").GetProperty("Initial")!
                .GetValue(Entries(update, "Pinned").GetValue(0))! == "微");
            File.Delete(Path.Combine(pins, "微信.url"));
            PumpUntil(() => Read(false) is { } update && Entries(update, "Pinned").Length == 0);
            Directory.Delete(Path.Combine(pins, "Nested"), true);
            Directory.Delete(pins);
            File.WriteAllText(pins, "occupied");
            snapshot = Read()!;
            Check((bool)snapshot.GetType().GetProperty("PinnedUnavailable")!.GetValue(snapshot)! && Entries(snapshot, "Menu").Length == 2,
                "A broken Pinned directory broke the root menu.");
            File.Delete(pins);
            snapshot = Read()!;
            Check(!(bool)snapshot.GetType().GetProperty("PinnedUnavailable")!.GetValue(snapshot)!, "Pinned did not recover.");
            Thread.Sleep(100); Read(false);
            Check(Read(false) is null, "A clean directory was read again.");
            foreach (var (name, initial) in new[] { ("apple", "A"), ("中文", "中"), ("😀tool", "😀"), ("e\u0301cole", "e\u0301") })
                Check((string)Type("ShortcutDirectory").GetMethod("GetInitial")!.Invoke(null, [name])! == initial, "Initial glyph failed.");

            shell = Activator.CreateInstance(System.Type.GetTypeFromProgID("WScript.Shell", true)!)!;
            dynamic link = ((dynamic)shell).CreateShortcut(Path.Combine(pins, "self.lnk"));
            link.TargetPath = Environment.ProcessPath!; link.Save(); Marshal.FinalReleaseComObject((object)link);
            snapshot = Read()!;
            var entry = Entries(snapshot, "Pinned").GetValue(0)!;
            Check(string.Equals((string?)Type("ShortcutEntry").GetProperty("ExecutablePath")!.GetValue(entry), Environment.ProcessPath,
                StringComparison.OrdinalIgnoreCase), "Executable link resolution failed.");
            var running = (Dictionary<int, string>)Type("ShortcutWindows").GetMethod("ReadProcesses")!.Invoke(null, [new[] { Environment.ProcessPath! }])!;
            Check(running.Count > 0, "An externally running process was missed.");
            Check(((Dictionary<int, string>)Type("ShortcutWindows").GetMethod("ReadProcesses")!.Invoke(null,
                [new[] { Path.Combine(root.FullName, Path.GetFileName(Environment.ProcessPath)!) }])!).Count == 0,
                "An unrelated executable with the same name was matched.");

            var layout = (FrameworkElement)bar.FindName("BarLayout");
            var panel = (Panel)bar.FindName("PinnedShortcutsArea");
            var many = Array.CreateInstance(Type("ShortcutEntry"), 40);
            for (var i = 0; i < many.Length; i++) many.SetValue(Activator.CreateInstance(Type("ShortcutEntry"), [$"app{i:D2}.lnk"]), i);
            var menuEntries = Entries(snapshot, "Menu");
            snapshot = Activator.CreateInstance(Type("ShortcutSnapshot"), [menuEntries, many, false, false])!;
            Type("MainWindow").GetMethod("ApplyShortcuts", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bar, [snapshot]);
            foreach (var width in new[] { 1000.0, 640.0, 4000.0 })
            {
                layout.Measure(new Size(width, 28)); layout.Arrange(new Rect(0, 0, width, 28)); layout.UpdateLayout();
                var hub = (FrameworkElement)bar.FindName("CentralHubButton");
                var clock = (FrameworkElement)bar.FindName("ClockArea");
                Check(Math.Abs(hub.TranslatePoint(new Point(), layout).X + hub.ActualWidth / 2 - width / 2) < .01, "Pins moved the hub.");
                Check(Math.Abs(clock.TranslatePoint(new Point(), layout).X + clock.ActualWidth - width) < .01, "Pins moved the clock.");
                var visible = panel.Children.Cast<FrameworkElement>().Where(child => child.Visibility == Visibility.Visible).ToArray();
                Check(visible.All(child => child.ActualWidth == 33 && child.TranslatePoint(new Point(), layout).X + 33 <= hub.TranslatePoint(new Point(), layout).X), "A partial/overlapping pin was visible.");
                var menu = new ContextMenu();
                Type("MainWindow").GetMethod("PopulateShortcutsMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bar, [menu]);
                var overflow = menu.Items.OfType<MenuItem>().FirstOrDefault(item => Equals(item.Header, "固定项目"));
                Check((overflow?.Items.Count ?? 0) == 40 - visible.Length, "Overflow pins were lost or duplicated.");
            }
            var target = new Window { Title = "Shortcut checks", Width = 260, Height = 100 };
            try
            {
                target.Show(); target.WindowState = WindowState.Minimized;
                var app = Activator.CreateInstance(Type("ShortcutApplications"))!;
                Type("ShortcutApplications").GetMethod("RememberForeground")!.Invoke(app, [new WindowInteropHelper(target).Handle]);
                var result = Result((Task)Type("ShortcutApplications").GetMethod("OpenAsync")!.Invoke(app, [entry, CancellationToken.None, (nint)0])!);
                Check(result!.ToString() is "Activated" or "Blocked", "An existing window was relaunched.");
                Check(target.WindowState != WindowState.Minimized, "The minimized window was not restored.");
            }
            finally { target.Close(); }
            var switched = Result((Task)Type("PinnedShortcuts").GetMethod("ReadAsync")!
                .Invoke(catalog, [true, Path.Combine(root.FullName, "Other")])!);
            Check(Directory.Exists(Path.Combine(root.FullName, "Other", "Pinned")) && Entries(switched!, "Pinned").Length == 0,
                "Switching folders retained the previous pins.");
            catalog.Dispose();
            Check(Read() is null, "Disposal did not stop directory reads.");
            bar.Show();
            Check(bar.IsVisible && new WindowInteropHelper(bar).Handle != 0, "The bar did not show.");
            bar.Close();
            var pendingRead = (Task)Type("MainWindow").GetField("_shortcutRead", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bar)!;
            PumpUntil(() => pendingRead.IsCompleted);
            var monitor = Type("MainWindow").GetField("_pinnedShortcuts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bar)!;
            Check(Type("PinnedShortcuts").GetField("_watcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(monitor) is null,
                "Closing the bar retained a watcher.");
            Check(!((DispatcherTimer)Type("MainWindow").GetField("_statusTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bar)!).IsEnabled,
                "Closing the bar retained a running status timer.");
            Console.WriteLine("Shortcut directories, watcher, cache, Unicode, overflow/recovery, process matching, activation and cleanup passed.");
        }
        finally
        {
            bar.Close(); catalog.Dispose();
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
            root.Delete(true);
        }
    }

    private static object? Result(Task task)
    {
        PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult();
        return task.GetType().GetProperty("Result")!.GetValue(task);
    }
    private static void PumpUntil(Func<bool> ready)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!ready())
        {
            Check(Environment.TickCount64 < deadline, "Timed out waiting for a shortcut change.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame); Thread.Sleep(10);
        }
    }
    private static void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
}

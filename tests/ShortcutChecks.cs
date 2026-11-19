#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property UseAppHost=false
#:property Nullable=enable

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

internal static class ShortcutChecks
{
    [STAThread]
    public static void Main(string[] args)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var directoryType = assembly.GetType("WindowsTopBar.ShortcutDirectory", true)!;
        var catalogType = assembly.GetType("WindowsTopBar.ShortcutCatalog", true)!;
        var entryType = assembly.GetType("WindowsTopBar.ShortcutEntry", true)!;
        var snapshotType = assembly.GetType("WindowsTopBar.ShortcutSnapshot", true)!;
        var read = catalogType.GetMethod("Read", [typeof(string)])!;
        var initial = directoryType.GetMethod("GetInitial")!;
        foreach (var (name, expected) in new[]
        {
            ("chrome", "C"), ("微信", "微"), ("_name", "_"), ("7zip", "7"),
            ("e\u0301ditor", "e\u0301"), ("😀tool", "😀"), ("", "")
        })
        {
            Check((string)initial.Invoke(null, [name])! == expected, $"Wrong initial for {name}.");
        }

        var folder = Directory.CreateTempSubdirectory("topbar-shortcuts-");
        using var changed = new AutoResetEvent(false);
        using var catalog = (IDisposable)Activator.CreateInstance(catalogType)!;
        EventHandler handler = (_, _) => changed.Set();
        catalogType.GetEvent("Changed")!.AddEventHandler(catalog, handler);
        try
        {
            var pinnedFolder = Path.Combine(folder.FullName, "Pinned");
            Read();
            Check(Directory.Exists(pinnedFolder), "The initial read did not create Pinned.");
            File.WriteAllText(Path.Combine(folder.FullName, "beta.LNK"), "");
            File.WriteAllText(Path.Combine(folder.FullName, "alpha.url"), "");
            File.WriteAllText(Path.Combine(folder.FullName, "ignore.txt"), "");
            Directory.CreateDirectory(Path.Combine(pinnedFolder, "Nested"));
            File.WriteAllText(Path.Combine(pinnedFolder, "Nested", "hidden.lnk"), "");
            var snapshot = Read();
            Check(Names(snapshot, "Menu").SequenceEqual(["alpha", "beta"]) && Entries(snapshot, "Pinned").Length == 0,
                "The root must be sorted, extension-filtered, and non-recursive.");

            // Ignore deeper file events rather than repeatedly reading unused subdirectories.
            Thread.Sleep(100);
            changed.Reset();
            File.WriteAllText(Path.Combine(pinnedFolder, "Nested", "another.url"), "");
            Check(!changed.WaitOne(250), "A nested file unexpectedly requested a refresh.");
            File.Delete(Path.Combine(pinnedFolder, "Nested", "another.url"));

            changed.Reset();
            File.Move(Path.Combine(folder.FullName, "beta.LNK"), Path.Combine(pinnedFolder, "beta.LNK"));
            Check(changed.WaitOne(3000), "Moving a shortcut did not notify the watcher.");
            snapshot = Read();
            Check(Names(snapshot, "Menu").SequenceEqual(["alpha"]) && Names(snapshot, "Pinned").SequenceEqual(["beta"]),
                "Moving into Pinned duplicated or lost a shortcut.");

            changed.Reset();
            File.Move(Path.Combine(pinnedFolder, "beta.LNK"), Path.Combine(pinnedFolder, "微信.lnk"));
            Check(changed.WaitOne(3000), "Renaming a pinned shortcut did not notify the watcher.");
            snapshot = Read();
            Check(Names(snapshot, "Pinned").SequenceEqual(["微信"]), "Renaming did not update pinned metadata.");
            Check((string)entryType.GetProperty("Initial")!.GetValue(Entries(snapshot, "Pinned").GetValue(0))! == "微",
                "Pinned shortcuts did not use the shared initial logic.");

            File.Move(Path.Combine(pinnedFolder, "微信.lnk"), Path.Combine(folder.FullName, "微信.lnk"));
            Check(Entries(Read(), "Pinned").Length == 0, "Moving back to the root did not unpin.");
            File.Delete(Path.Combine(pinnedFolder, "Nested", "hidden.lnk"));
            Directory.Delete(Path.Combine(pinnedFolder, "Nested"));
            Directory.Delete(pinnedFolder);
            snapshot = Read();
            Check(Directory.Exists(pinnedFolder) && !(bool)snapshotType.GetProperty("PinnedUnavailable")!.GetValue(snapshot)!,
                "A missing Pinned directory was not recreated.");

            // A file occupying the reserved folder name must not break root shortcuts.
            Directory.Delete(pinnedFolder);
            File.WriteAllText(pinnedFolder, "");
            snapshot = Read();
            Check((bool)snapshotType.GetProperty("PinnedUnavailable")!.GetValue(snapshot)! &&
                !(bool)snapshotType.GetProperty("MenuUnavailable")!.GetValue(snapshot)! && Entries(snapshot, "Menu").Length == 2,
                "A broken Pinned folder disabled the root menu.");
            File.Delete(pinnedFolder);
            snapshot = Read();
            Check(!(bool)snapshotType.GetProperty("PinnedUnavailable")!.GetValue(snapshot)!, "Pinned did not recover.");

            changed.Reset();
            File.WriteAllText(Path.Combine(pinnedFolder, "added.URL"), "");
            Check(changed.WaitOne(3000), "Creating a pin did not notify the watcher.");
            Check(Names(Read(), "Pinned").SequenceEqual(["added"]), "A new pin was not read.");
            changed.Reset();
            File.Delete(Path.Combine(pinnedFolder, "added.URL"));
            Check(changed.WaitOne(3000), "Deleting a pin did not notify the watcher.");
            Check(Entries(Read(), "Pinned").Length == 0, "Deleting a pin left stale entries.");

            var oldWatcher = catalogType.GetField("_watcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(catalog)!;
            var secondFolder = Path.Combine(folder.FullName, "Other");
            read.Invoke(catalog, [secondFolder]);
            Check(!((FileSystemWatcher)oldWatcher).EnableRaisingEvents, "Switching folders left the old watcher active.");
            Check(Directory.Exists(Path.Combine(secondFolder, "Pinned")), "Switching folders did not create Pinned.");

            var invalid = read.Invoke(catalog, [Path.Combine(folder.FullName, "alpha.url", "invalid")])!;
            Check((bool)snapshotType.GetProperty("MenuUnavailable")!.GetValue(invalid)!, "An invalid root was accepted.");
            snapshot = Read();
            catalogType.GetEvent("Changed")!.RemoveEventHandler(catalog, handler);
            catalog.Dispose();
            Check(catalogType.GetField("_watcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(catalog) is null,
                "Disposing the catalog left its watcher alive.");
            read.Invoke(catalog, [folder.FullName]);
            Check(catalogType.GetField("_watcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(catalog) is null,
                "A background read recreated the watcher after disposal.");
        }
        finally
        {
            catalogType.GetEvent("Changed")!.RemoveEventHandler(catalog, handler);
            catalog.Dispose();
            folder.Delete(recursive: true);
        }

        var windowType = assembly.GetType("WindowsTopBar.MainWindow", true)!;
        var window = (Window)Activator.CreateInstance(windowType)!;
        try
        {
            var layout = (FrameworkElement)window.FindName("BarLayout");
            var panel = (Panel)window.FindName("PinnedShortcutsArea");
            var hub = (FrameworkElement)window.FindName("CentralHubButton");
            var clock = (FrameworkElement)window.FindName("ClockArea");
            var launcher = (FrameworkElement)window.FindName("ShortcutsButton");
            var entries = Array.CreateInstance(entryType, 30);
            for (var i = 0; i < entries.Length; i++)
            {
                entries.SetValue(Activator.CreateInstance(entryType, [$"app{i:D2}.lnk"]), i);
            }
            var root = Array.CreateInstance(entryType, 1);
            root.SetValue(Activator.CreateInstance(entryType, ["regular_name.url"]), 0);
            var snapshot = Activator.CreateInstance(snapshotType, [folder.FullName, root, entries, false, false])!;
            windowType.GetMethod("ApplyShortcuts", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [snapshot]);
            foreach (var (width, offset) in new[] { (1000.0, 0.0), (640.0, 0.0), (640.0, 20.0), (1000.0, -20.0) })
            {
                windowType.GetMethod("ApplyCentralHubOffset", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(window, [offset]);
                layout.Measure(new Size(width, 28));
                layout.Arrange(new Rect(0, 0, width, 28));
                layout.UpdateLayout();
                var visible = (int)panel.GetType().GetProperty("VisibleCount")!.GetValue(panel)!;
                Check(visible > 0 && visible < entries.Length, "The overflow count was not computed.");
                var hubLeft = LeftOf(hub);
                Check(Math.Abs(hubLeft + hub.ActualWidth / 2 - (width / 2 + offset)) < 0.01, "Pins moved the hub.");
                Check(Math.Abs(LeftOf(clock) + clock.ActualWidth - width) < 0.01, "Pins moved the clock.");
                Check(Math.Abs(LeftOf((FrameworkElement)panel.Children[0]) - launcher.ActualWidth) < 0.01,
                    "Pinned shortcuts must begin immediately after Quick Launch.");
                foreach (FrameworkElement button in panel.Children)
                {
                    if (button.Visibility == Visibility.Visible)
                    {
                        Check(LeftOf(button) + button.ActualWidth <= hubLeft + 0.01, "A pinned button overlaps the hub.");
                    }
                }
                Check(panel.Children.Cast<UIElement>().Count(child => child.Visibility == Visibility.Visible) == visible,
                    "An overflowing pinned button stayed visible.");
                var menu = new ContextMenu();
                windowType.GetMethod("PopulateShortcutsMenu", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(window, [menu]);
                Check(menu.Items.Count == 2 && ((TextBlock)((MenuItem)menu.Items[0]).Header).Text == "regular_name",
                    "Normal root entries or literal underscores were lost.");
                var overflow = (MenuItem)menu.Items[1];
                Check(Equals(overflow.Header, "固定项目") && overflow.Items.Count == entries.Length - visible,
                    "The menu must contain exactly the overflowing pins.");
                Check(((TextBlock)((MenuItem)overflow.Items[0]).Header).Text == $"app{visible:D2}",
                    "A visible pin was duplicated in the overflow menu.");
            }
            var first = (Button)panel.Children[0];
            Check(Equals(first.ToolTip, "app00") && first.Content is TextBlock { Text: "A" },
                "The pinned template lost its full-name tooltip or initial.");

            // Enlarging must bring hidden buttons back; an empty folder must remove their hit targets.
            layout.Measure(new Size(4000, 28));
            layout.Arrange(new Rect(0, 0, 4000, 28));
            layout.UpdateLayout();
            Check(panel.Children.Cast<UIElement>().All(child => child.Visibility == Visibility.Visible), "Hidden pins did not return.");
            var fullMenu = new ContextMenu();
            windowType.GetMethod("PopulateShortcutsMenu", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, [fullMenu]);
            Check(fullMenu.Items.Count == 1, "Pins that all fit still appeared in the menu.");

            // At a fractional boundary, never draw or hit-test a partial button.
            panel.GetType().GetMethod("SetContentWidth")!.Invoke(panel, [32.99]);
            panel.UpdateLayout();
            Check((int)panel.GetType().GetProperty("VisibleCount")!.GetValue(panel)! == 0 &&
                panel.Children.Cast<UIElement>().All(child => child.Visibility == Visibility.Hidden),
                "A partial pinned button remained visible.");
            panel.GetType().GetMethod("SetContentWidth")!.Invoke(panel, [33.0]);
            panel.UpdateLayout();
            Check((int)panel.GetType().GetProperty("VisibleCount")!.GetValue(panel)! == 1,
                "A complete button did not return at the exact boundary.");
            var rootFailure = Activator.CreateInstance(snapshotType,
                [folder.FullName, Array.CreateInstance(entryType, 0), entries, true, false])!;
            windowType.GetMethod("ApplyShortcuts", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, [rootFailure]);
            var failureMenu = new ContextMenu();
            windowType.GetMethod("PopulateShortcutsMenu", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, [failureMenu]);
            Check(!((MenuItem)failureMenu.Items[0]).IsEnabled && panel.Children.Count == 30 &&
                Equals(((MenuItem)failureMenu.Items[1]).Header, "固定项目"),
                "A root failure removed the still-available pins.");
            var pinnedFailure = Activator.CreateInstance(snapshotType,
                [folder.FullName, root, Array.CreateInstance(entryType, 0), false, true])!;
            windowType.GetMethod("ApplyShortcuts", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, [pinnedFailure]);
            windowType.GetMethod("PopulateShortcutsMenu", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, [failureMenu]);
            Check(((TextBlock)((MenuItem)failureMenu.Items[0]).Header).Text == "regular_name" &&
                !((MenuItem)((MenuItem)failureMenu.Items[1]).Items[0]).IsEnabled,
                "A Pinned failure removed the still-available root entries.");
            var empty = Activator.CreateInstance(snapshotType,
                [folder.FullName, Array.CreateInstance(entryType, 0), Array.CreateInstance(entryType, 0), false, false])!;
            windowType.GetMethod("ApplyShortcuts", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [empty]);
            Check(panel.Children.Count == 0, "Unpinning all entries left stale buttons.");

            double LeftOf(FrameworkElement element) => element.TranslatePoint(new Point(), layout).X;
        }
        finally
        {
            window.Close();
        }

        // Exercise actual worker -> debounce timer -> dispatcher refresh, without showing an AppBar.
        var liveFolder = Directory.CreateTempSubdirectory("topbar-refresh-");
        var liveWindow = (Window)Activator.CreateInstance(windowType)!;
        try
        {
            var pins = Path.Combine(liveFolder.FullName, "Pinned");
            Directory.CreateDirectory(pins);
            var empty = Activator.CreateInstance(snapshotType,
                [liveFolder.FullName, Array.CreateInstance(entryType, 0), Array.CreateInstance(entryType, 0), false, false])!;
            windowType.GetMethod("ApplyShortcuts", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(liveWindow, [empty]);
            var refresh = windowType.GetMethod("RefreshShortcutsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var readTask = (Task)refresh.Invoke(liveWindow, [false])!;
            PumpUntil(() => readTask.IsCompleted);
            readTask.GetAwaiter().GetResult();
            var livePanel = (Panel)liveWindow.FindName("PinnedShortcutsArea");
            for (var i = 0; i < 12; i++)
            {
                File.WriteAllText(Path.Combine(pins, $"app{i:D2}.url"), "");
            }
            PumpUntil(() => livePanel.Children.Count == 12);
            File.Move(Path.Combine(pins, "app00.url"), Path.Combine(liveFolder.FullName, "moved.url"));
            PumpUntil(() => livePanel.Children.Count == 11);
            Check(Names(windowType.GetField("_shortcuts", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(liveWindow)!,
                "Menu").SequenceEqual(["moved"]), "The watcher did not update both directory groups.");

            File.Move(Path.Combine(pins, "app01.url"), Path.Combine(pins, "微信.url"));
            PumpUntil(() => livePanel.Children.Cast<Button>().Any(button => Equals(button.ToolTip, "微信")));
            var pending = (Task)refresh.Invoke(liveWindow, [false])!;
            liveWindow.Close();
            PumpUntil(() => pending.IsCompleted);
            pending.GetAwaiter().GetResult();
            var liveCatalog = windowType.GetField("_shortcutCatalog", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(liveWindow)!;
            Check(catalogType.GetField("_watcher", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(liveCatalog) is null,
                "Closing during a refresh left a watcher alive.");
            Check(!((DispatcherTimer)windowType.GetField("_shortcutRefreshTimer", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(liveWindow)!).IsEnabled, "Closing left the debounce timer running.");
            Check(catalogType.GetField("Changed", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(liveCatalog) is null,
                "Closing left the directory event subscribed.");
        }
        finally
        {
            liveWindow.Close();
            liveFolder.Delete(recursive: true);
        }
        Console.WriteLine("Shortcut directory, watcher, glyph, overflow, async refresh, and cleanup checks passed.");

        object Read() => read.Invoke(catalog, [folder.FullName])!;
        Array Entries(object snapshot, string property) => (Array)snapshotType.GetProperty(property)!.GetValue(snapshot)!;
        string[] Names(object snapshot, string property) => Entries(snapshot, property).Cast<object>()
            .Select(entry => (string)entryType.GetProperty("Name")!.GetValue(entry)!).ToArray();
    }

    private static void PumpUntil(Func<bool> completed)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!completed())
        {
            Check(Environment.TickCount64 < deadline, "Timed out waiting for an asynchronous directory refresh.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
    }

    private static void Check(bool passed, string message)
    {
        if (!passed)
        {
            throw new InvalidOperationException(message);
        }
    }
}

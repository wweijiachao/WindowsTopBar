#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property UseAppHost=false

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

// Pure selection checks: fake handles only, no process queries, windows, or desktop interaction.
var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
var candidate = assembly.GetType("WindowsTopBar.ShortcutWindow", true)!;
var select = assembly.GetType("WindowsTopBar.ShortcutApplications", true)!
    .GetMethod("SelectWindow", BindingFlags.Static | BindingFlags.NonPublic)!;
var windows = Array.CreateInstance(candidate, 3);
windows.SetValue(Activator.CreateInstance(candidate, [(nint)10, 1]), 0);
windows.SetValue(Activator.CreateInstance(candidate, [(nint)20, 2]), 1);
windows.SetValue(Activator.CreateInstance(candidate, [(nint)11, 1]), 2);

Check("foreground wins over recent history", [1], [11, 10], 10, 10);
Check("another app is foreground", [1], [20, 11], 20, 11);
Check("stale history falls back to current windows", [1], [99], 20, 10);
Check("no history", [1], [], 20, 10);
Check("multiple processes belonging to the app", [1, 2], [11], 20, 20);
Check("no matching process", [], [10], 10, 0);
var resolve = assembly.GetType("WindowsTopBar.MessengerWindows", true)!
    .GetMethod("ResolveInteractionForeground", BindingFlags.Static | BindingFlags.NonPublic)!;
CheckForeground("bar took focus", 100, true, 10, 10);
CheckForeground("own popup took focus", 101, true, 11, 11);
CheckForeground("system foreground is temporarily unavailable", 0, false, 10, 10);
CheckForeground("another application became foreground", 20, false, 10, 20);
CheckForeground("no external window recorded", 100, true, 0, 0);
Console.WriteLine("Shortcut selection and bar/popup foreground fallback checks passed.");

void CheckForeground(string name, nint current, bool ownProcess, nint previous, nint expected)
{
    var actual = (nint)resolve.Invoke(null, [current, ownProcess, previous])!;
    if (actual != expected) throw new InvalidOperationException($"{name}: expected {expected}, got {actual}.");
    if (actual == 10 || actual == 11) Check(name + " selects original app", [1], [20], actual, actual);
}

void Check(string name, int[] processes, nint[] recent, nint foreground, nint expected)
{
    var actual = (nint)select.Invoke(null, [windows, new HashSet<int>(processes), recent, foreground])!;
    if (actual != expected) throw new InvalidOperationException($"{name}: expected {expected}, got {actual}.");
}

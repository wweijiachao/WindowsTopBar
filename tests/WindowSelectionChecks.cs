#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false

using System;
using System.IO;
using System.Reflection;

var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
var appBar = assembly.GetType("WindowsTopBar.AppBar", throwOnError: true)!;
var rect = appBar.GetNestedType("Rect", BindingFlags.NonPublic)!;
var candidate = appBar.GetNestedType("WindowCandidate", BindingFlags.NonPublic)!;
var select = appBar.GetMethod("SelectWindow", BindingFlags.NonPublic | BindingFlags.Static)!;

var workArea = Bounds(0, 28, 1920, 1040);
var left = Window(1, false, Bounds(2, 30, 637, 1038));
var right = Window(2, false, Bounds(643, 30, 1918, 1038));
var frontMaximized = Window(3, true, workArea);
var backMaximized = Window(4, true, workArea);

Check("topmost maximized window", 500, 3, 0, frontMaximized, backMaximized);
Check("focus the left window of a foreground split", 500, 1, 2, right, left, frontMaximized);
Check("focus the right window of a foreground split", 900, 2, 1, left, right, frontMaximized);
Check("switch to a pair without a maximized window", 900, 2, 1, left, right);
Check("ignore a split behind a maximized window", 500, 3, 0, frontMaximized, left, right);
Check("avoid pairing windows across a maximized window", 500, 3, 0, left, frontMaximized, right);
Check("keep the current focus for a lone snapped window", 500, 0, 0, left);
Check("fall back to maximized for a gap", 500, 3, 0, left, Window(5, false, Bounds(800, 28, 1920, 1040)), frontMaximized);
Check("fall back to maximized for a short window", 500, 3, 0, left, Window(5, false, Bounds(643, 28, 1920, 720)), frontMaximized);
Check("avoid choosing between overlapping layouts", 500, 3, 0, left, right,
    Window(5, false, Bounds(0, 28, 640, 1040)), frontMaximized);

Console.WriteLine("Window selection checks passed.");

object Bounds(int left, int top, int right, int bottom) =>
    Activator.CreateInstance(rect, left, top, right, bottom)!;

object Window(int handle, bool maximized, object bounds) =>
    Activator.CreateInstance(candidate, (nint)handle, maximized, bounds)!;

void Check(string scenario, int screenX, int target, int companion, params object[] windows)
{
    var candidates = Array.CreateInstance(candidate, windows.Length);
    Array.Copy(windows, candidates, windows.Length);
    var actual = ((nint Target, nint Companion))select.Invoke(null, [candidates, workArea, 16, screenX])!;
    if (actual != ((nint)target, (nint)companion))
    {
        throw new InvalidOperationException($"{scenario}: expected ({target}, {companion}), got {actual}.");
    }
}

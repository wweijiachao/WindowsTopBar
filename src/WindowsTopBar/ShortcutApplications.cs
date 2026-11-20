using System.Windows;

namespace WindowsTopBar;

internal enum ShortcutOpenResult { Launched, Activated, Blocked }

internal sealed class ShortcutApplications
{
    public static readonly DependencyProperty IsRunningProperty = DependencyProperty.RegisterAttached(
        "IsRunning", typeof(bool), typeof(ShortcutApplications), new PropertyMetadata(false));

    public static bool GetIsRunning(DependencyObject element) => (bool)element.GetValue(IsRunningProperty);
    public static void SetIsRunning(DependencyObject element, bool value) => element.SetValue(IsRunningProperty, value);

    private readonly object _gate = new();
    private readonly List<nint> _recentWindows = [];

    public void RememberForeground(nint window)
    {
        if (window == 0) return;
        lock (_gate)
        {
            _recentWindows.Remove(window);
            _recentWindows.Insert(0, window);
            if (_recentWindows.Count > 128) _recentWindows.RemoveAt(128);
        }
    }

    public static HashSet<string> ReadRunning(ShortcutEntry[] entries) =>
        ShortcutWindows.ReadProcesses(entries.Select(entry => entry.ExecutablePath).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
            .Values.ToHashSet(StringComparer.OrdinalIgnoreCase);

    public nint FindWindow(string? executable)
    {
        if (executable is null) return 0;
        var processes = ShortcutWindows.ReadProcesses([executable]);
        nint[] recent;
        lock (_gate) { recent = _recentWindows.ToArray(); }
        return SelectWindow(ShortcutWindows.ReadWindows(), processes.Keys.ToHashSet(), recent);
    }

    public async Task<ShortcutOpenResult> OpenAsync(ShortcutEntry entry, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var window = await Task.Run(() => FindWindow(entry.ExecutablePath), token);
        if (window != 0)
        {
            var activation = await ShortcutWindows.ActivateAsync(window, token);
            if (activation == ShortcutActivation.Activated) return ShortcutOpenResult.Activated;
            if (activation == ShortcutActivation.Blocked) return ShortcutOpenResult.Blocked;
        }
        // No usable window: retain the original shortcut's arguments and the app's own tray/launch behavior.
        await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            ShortcutDirectory.Launch(entry.Path);
        }, token);
        return ShortcutOpenResult.Launched;
    }

    internal static nint SelectWindow(ShortcutWindow[] windows, HashSet<int> processIds, nint[] recent)
    {
        var candidates = windows.Where(window => processIds.Contains(window.ProcessId)).ToArray();
        foreach (var handle in recent)
        {
            if (candidates.Any(window => window.Handle == handle)) return handle;
        }
        return candidates.FirstOrDefault()?.Handle ?? 0;
    }
}

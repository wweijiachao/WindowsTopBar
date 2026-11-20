namespace WindowsTopBar;

internal enum ShortcutOpenResult { Launched, Activated, Minimized, Blocked }

internal sealed class ShortcutApplications
{
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

    private nint FindWindow(string? executable, nint foreground)
    {
        if (executable is null) return 0;
        var processes = ShortcutWindows.ReadProcesses([executable]);
        nint[] recent;
        lock (_gate) { recent = _recentWindows.ToArray(); }
        return SelectWindow(ShortcutWindows.ReadWindows(), processes.Keys.ToHashSet(), recent, foreground);
    }

    public async Task<ShortcutOpenResult> OpenAsync(ShortcutEntry entry, CancellationToken token, nint foreground)
    {
        token.ThrowIfCancellationRequested();
        var window = await Task.Run(() => FindWindow(entry.ExecutablePath, foreground), token);
        if (window != 0)
        {
            var activation = await ShortcutWindows.ToggleAsync(window, foreground, token);
            if (activation == ShortcutActivation.Activated) return ShortcutOpenResult.Activated;
            if (activation == ShortcutActivation.Minimized) return ShortcutOpenResult.Minimized;
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

    internal static nint SelectWindow(ShortcutWindow[] windows, HashSet<int> processIds, nint[] recent, nint foreground)
    {
        var candidates = windows.Where(window => processIds.Contains(window.ProcessId)).ToArray();
        // Prefer the actual foreground even when another window of the same app appears earlier in history.
        if (candidates.Any(window => window.Handle == foreground)) return foreground;
        foreach (var handle in recent)
        {
            if (candidates.Any(window => window.Handle == handle)) return handle;
        }
        return candidates.FirstOrDefault()?.Handle ?? 0;
    }
}

using System.IO;

namespace WindowsTopBar;

internal sealed record ShortcutEntry(string Path)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    public string Initial => ShortcutDirectory.GetInitial(Name);
    public string? ExecutablePath { get; init; }
}

internal sealed record ShortcutSnapshot(ShortcutEntry[] Menu, ShortcutEntry[] Pinned,
    bool MenuUnavailable = false, bool PinnedUnavailable = false);

internal sealed class PinnedShortcuts : IDisposable
{
    private readonly SemaphoreSlim _reads = new(1);
    private readonly object _watchGate = new();
    private FileSystemWatcher? _watcher;
    private string? _folder;
    private int _changed = 1;
    private bool _disposed;

    // Reuse the status tick to merge bursts. Clean directories do no disk/COM work.
    public async Task<ShortcutSnapshot?> ReadAsync(bool force = false, string? folder = null)
    {
        await _reads.WaitAsync();
        try
        {
            var changed = Interlocked.Exchange(ref _changed, 0);
            if (_disposed || (!force && changed == 0)) return null;
            ShortcutSnapshot snapshot;
            try
            {
                if (force || _folder is null || folder is not null)
                    _folder = await Task.Run(() => System.IO.Path.TrimEndingDirectorySeparator(
                        System.IO.Path.GetFullPath(folder ?? ShortcutDirectory.GetConfiguredFolder())));
                snapshot = await Task.Run(() =>
                {
                    try { Directory.CreateDirectory(_folder!); }
                    catch (Exception error) when (IsDirectoryError(error)) { }
                    Watch(_folder!);
                    var menu = ReadEntries(_folder!, out var rootError);
                    var pins = ReadEntries(System.IO.Path.Combine(_folder!, "Pinned"), out var pinError);
                    return new ShortcutSnapshot(menu, pins, rootError, pinError);
                });
            }
            catch (Exception error) when (IsDirectoryError(error))
            {
                StopWatching();
                return new([], [], true, true);
            }
            if (_disposed) return null;
            var entries = await ShellShortcut.ResolveAsync(snapshot.Menu.Concat(snapshot.Pinned).ToArray());
            return snapshot with { Menu = entries.Take(snapshot.Menu.Length).ToArray(), Pinned = entries.Skip(snapshot.Menu.Length).ToArray() };
        }
        finally { _reads.Release(); }
    }

    private static ShortcutEntry[] ReadEntries(string folder, out bool unavailable)
    {
        try
        {
            var entries = ShortcutDirectory.List(folder).Select(path => new ShortcutEntry(path)).ToArray();
            unavailable = false;
            return entries;
        }
        catch (Exception error) when (IsDirectoryError(error))
        {
            unavailable = true;
            return [];
        }
    }

    private void Watch(string folder)
    {
        lock (_watchGate)
        {
            if (_disposed || string.Equals(_watcher?.Path, folder, StringComparison.OrdinalIgnoreCase)) return;
            StopWatching();
            try
            {
                _watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
                };
                _watcher.Created += OnChanged;
                _watcher.Deleted += OnChanged;
                _watcher.Changed += OnChanged;
                _watcher.Renamed += OnRenamed;
                _watcher.Error += OnError;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception error) when (IsDirectoryError(error)) { StopWatching(); }
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        var root = ((FileSystemWatcher)sender).Path;
        var parent = System.IO.Path.GetDirectoryName(e.FullPath);
        if (string.Equals(parent, root, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parent, System.IO.Path.Combine(root, "Pinned"), StringComparison.OrdinalIgnoreCase))
            Interlocked.Exchange(ref _changed, 1);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        OnChanged(sender, e);
        OnChanged(sender, new FileSystemEventArgs(WatcherChangeTypes.Deleted,
            System.IO.Path.GetDirectoryName(e.OldFullPath)!, System.IO.Path.GetFileName(e.OldFullPath)));
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        StopWatching();
        Interlocked.Exchange(ref _changed, 1);
    }

    private static bool IsDirectoryError(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private void StopWatching()
    {
        lock (_watchGate)
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    public void Dispose()
    {
        lock (_watchGate)
        {
            _disposed = true;
            StopWatching();
        }
    }
}

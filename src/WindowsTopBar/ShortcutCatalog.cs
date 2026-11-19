using System.IO;

namespace WindowsTopBar;

internal sealed record ShortcutEntry(string Path)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    public string Initial => ShortcutDirectory.GetInitial(Name);
}

internal sealed record ShortcutSnapshot(string? Folder, ShortcutEntry[] Menu, ShortcutEntry[] Pinned,
    bool MenuUnavailable = false, bool PinnedUnavailable = false);

// Directory reads run in the background; the window marshals change notifications to its dispatcher.
internal sealed class ShortcutCatalog : IDisposable
{
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private bool _resetWatcher;
    private bool _disposed;

    public event EventHandler? Changed;

    public ShortcutSnapshot Read() => Read(ShortcutDirectory.GetConfiguredFolder);

    public ShortcutSnapshot Read(string folder) => Read(() => folder);

    private ShortcutSnapshot Read(Func<string> resolveFolder)
    {
        string folder;
        try
        {
            folder = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(resolveFolder()));
        }
        catch (Exception error) when (IsDirectoryError(error))
        {
            StopWatching();
            return new(null, [], [], MenuUnavailable: true, PinnedUnavailable: true);
        }

        // A failure in one directory must not prevent trying the other.
        // Create the root before installing the watcher so the initial read is covered by events.
        try { Directory.CreateDirectory(folder); }
        catch (Exception error) when (IsDirectoryError(error)) { }
        Watch(folder);
        var menu = ReadEntries(folder, out var menuUnavailable);
        var pinned = ReadEntries(ShortcutDirectory.GetPinnedFolder(folder), out var pinnedUnavailable);
        return new(folder, menu, pinned, menuUnavailable, pinnedUnavailable);
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
        lock (_gate)
        {
            if (_disposed || (!_resetWatcher && _watcher is not null &&
                string.Equals(_watcher.Path, folder, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            ReleaseWatcher();
            try
            {
                _watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                };
                _watcher.Created += OnChanged;
                _watcher.Deleted += OnChanged;
                _watcher.Renamed += OnRenamed;
                _watcher.Error += OnError;
                _watcher.EnableRaisingEvents = true;
                _resetWatcher = false;
            }
            catch (Exception error) when (IsDirectoryError(error))
            {
                // A readable folder can still reject watching. Opening the menu retries the read/watch.
                ReleaseWatcher();
            }
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (sender is FileSystemWatcher watcher && IsRelevant(watcher.Path, e.FullPath))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (sender is FileSystemWatcher watcher &&
            (IsRelevant(watcher.Path, e.FullPath) || IsRelevant(watcher.Path, e.OldFullPath)))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool IsRelevant(string folder, string path)
    {
        var parent = System.IO.Path.GetDirectoryName(path);
        return string.Equals(parent, folder, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parent, ShortcutDirectory.GetPinnedFolder(folder), StringComparison.OrdinalIgnoreCase);
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        lock (_gate)
        {
            _resetWatcher = true;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsDirectoryError(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private void StopWatching()
    {
        lock (_gate)
        {
            ReleaseWatcher();
        }
    }

    private void ReleaseWatcher()
    {
        if (_watcher is null)
        {
            return;
        }
        _watcher.Created -= OnChanged;
        _watcher.Deleted -= OnChanged;
        _watcher.Renamed -= OnRenamed;
        _watcher.Error -= OnError;
        _watcher.Dispose();
        _watcher = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            ReleaseWatcher();
        }
    }
}

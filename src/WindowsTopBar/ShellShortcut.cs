using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace WindowsTopBar;

internal static class ShellShortcut
{
    // Shell COM work stays on a background STA and never resolves links over the network or shows dialogs.
    public static Task<ShortcutEntry[]> ResolveAsync(ShortcutEntry[] entries)
    {
        if (!entries.Any(entry => Path.GetExtension(entry.Path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult(entries);
        var completion = new TaskCompletionSource<ShortcutEntry[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(entries.Select(Resolve).ToArray()); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = "Shortcut targets" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static ShortcutEntry Resolve(ShortcutEntry entry)
    {
        if (!Path.GetExtension(entry.Path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return entry;
        }
        object? link = null;
        try
        {
            link = new ShellLink();
            ((IPersistFile)link).Load(entry.Path, 0); // STGM_READ
            var path = new StringBuilder(32768);
            ((IShellLinkW)link).GetPath(path, path.Capacity, 0, 4); // SLGP_RAWPATH; no Resolve call
            return entry with { ExecutablePath = NormalizeExecutable(path.ToString()) };
        }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            return entry;
        }
        finally
        {
            if (link is not null) Marshal.FinalReleaseComObject(link);
        }
    }

    internal static string? NormalizeExecutable(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path);
        if (!Path.IsPathFullyQualified(path) || !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        // These executable hosts identify a shell/script/document, rather than the requested application.
        var host = Path.GetFileNameWithoutExtension(path);
        if (new[] { "cmd", "powershell", "pwsh", "wscript", "cscript", "rundll32", "mshta", "explorer" }
            .Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }
        return Path.GetFullPath(path);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    // Vtable order follows IShellLinkW; only GetPath is needed for application-level matching.
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, nint findData, uint flags);
    }
}

using System.Diagnostics;
using System.IO;

namespace WindowsTopBar;

internal static class ShortcutDirectory
{
    public static string Folder { get; } = Path.Combine(Path.GetDirectoryName(AppearanceSettings.FilePath)!, "Shortcuts");

    public static string[] List(string folder)
    {
        Directory.CreateDirectory(folder);
        return Directory.EnumerateFiles(folder)
            .Where(path => Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static void Launch(string path)
    {
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}

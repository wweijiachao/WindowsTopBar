using System.Diagnostics;
using System.IO;

namespace WindowsTopBar;

internal static class ShortcutDirectory
{
    public static string GetConfiguredFolder()
    {
        var configured = AppearanceSettings.Load(AppearanceSettings.FilePath).ShortcutFolder;
        var path = string.IsNullOrWhiteSpace(configured) ? AppearanceSettings.DefaultShortcutFolder : configured;
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path), AppearanceSettings.Folder);
    }

    public static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Launch(folder);
    }

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

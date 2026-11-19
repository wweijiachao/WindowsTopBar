using System.Diagnostics;
using System.Globalization;
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

    public static string GetPinnedFolder(string folder) => Path.Combine(folder, "Pinned");

    public static string GetInitial(string name)
    {
        var initial = name.Length == 0 ? string.Empty : StringInfo.GetNextTextElement(name);
        return initial.Length == 1 && initial[0] is >= 'a' and <= 'z'
            ? initial.ToUpperInvariant() : initial;
    }

    public static string[] List(string folder)
    {
        Directory.CreateDirectory(folder);
        return Directory.EnumerateFiles(folder)
            .Where(path => Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
    }

    public static void Launch(string path)
    {
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}

using System.IO;
using System.Text.Json;

namespace WindowsTopBar;

internal static class PinnedShortcutOrder
{
    // Input already uses filename order. Stable sorting preserves that order for unlisted entries.
    public static ShortcutEntry[] Apply(string folder, ShortcutEntry[] entries)
    {
        var path = Path.Combine(folder, "order.json");
        if (entries.Length == 0 || !File.Exists(path)) return entries;
        try
        {
            var names = JsonSerializer.Deserialize<string?[]>(File.ReadAllText(path));
            if (names is null) return entries;
            var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < names.Length; i++)
                if (!string.IsNullOrEmpty(names[i])) positions.TryAdd(names[i]!, i);
            return entries.OrderBy(entry => positions.GetValueOrDefault(Path.GetFileName(entry.Path), int.MaxValue)).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return entries;
        }
    }
}

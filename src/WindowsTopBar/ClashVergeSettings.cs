using System.IO;
using System.Text.Json;

namespace WindowsTopBar;

internal static class ClashVergeSettings
{
    public static string? ReadTooltip(string folder, string? version)
    {
        bool? proxy = null, tun = null;
        try
        {
            foreach (var line in File.ReadLines(Path.Combine(folder, "verge.yaml")))
            {
                if (line.StartsWith("enable_system_proxy:", StringComparison.Ordinal) &&
                    bool.TryParse(line.AsSpan("enable_system_proxy:".Length), out var proxyEnabled))
                {
                    proxy = proxyEnabled;
                }
                if (line.StartsWith("enable_tun_mode:", StringComparison.Ordinal) &&
                    bool.TryParse(line.AsSpan("enable_tun_mode:".Length), out var tunEnabled))
                {
                    tun = tunEnabled;
                }
                if (proxy.HasValue && tun.HasValue)
                {
                    break;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var profile = ReadProfileName(Path.Combine(folder, "profiles.yaml"));
        var title = string.IsNullOrWhiteSpace(version) ? "Clash Verge" : $"Clash Verge {version}";
        return $"{title}\n系统代理: {SwitchText(proxy)}\nTUN: {SwitchText(tun)}（配置）\n订阅: {profile ?? "未知"}";
    }

    private static string? ReadProfileName(string path)
    {
        try
        {
            // Clash Verge writes current before items, with each item starting at '- uid:'.
            string? current = null;
            var selected = false;
            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("current:", StringComparison.Ordinal))
                {
                    current = ReadScalar(line["current:".Length..]);
                }
                else if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    selected = current is not null && line.StartsWith("- uid:", StringComparison.Ordinal) &&
                        ReadScalar(line["- uid:".Length..]) == current;
                }
                else if (selected && line.StartsWith("  name:", StringComparison.Ordinal))
                {
                    return ReadScalar(line["  name:".Length..]);
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static string? ReadScalar(string value)
    {
        value = value.Trim();
        if (value.Length == 0 || value is "null" or "~" || value[0] is '|' or '>')
        {
            return null;
        }
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
        {
            return value[1..^1].Replace("''", "'");
        }
        if (value[0] == '"')
        {
            try
            {
                return JsonSerializer.Deserialize<string>(value);
            }
            catch (JsonException)
            {
                return null;
            }
        }
        return value;
    }

    private static string SwitchText(bool? value) => value switch { true => "on", false => "off", null => "未知" };
}

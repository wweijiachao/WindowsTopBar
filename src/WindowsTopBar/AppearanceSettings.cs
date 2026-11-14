using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsTopBar;

[JsonConverter(typeof(JsonStringEnumConverter<TopBarTheme>))]
internal enum TopBarTheme { System, Dark, Light }

internal sealed record AppearanceSettings
{
    public TopBarTheme Theme { get; init; } = TopBarTheme.System;
    public bool AcrylicEnabled { get; init; } = true;

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WindowsTopBar", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppearanceSettings Load(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            var settings = JsonSerializer.Deserialize<AppearanceSettings>(file, JsonOptions);
            return settings is not null && Enum.IsDefined(settings.Theme) ? settings : new();
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return new();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.TraceWarning("Could not load WindowsTopBar settings: {0}", error.Message);
            return new();
        }
    }

    public bool TrySave(string path)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Could not save WindowsTopBar settings: {0}", error.Message);
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("Could not remove temporary settings: {0}", cleanupError.Message);
            }

            return false;
        }
    }
}

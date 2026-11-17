using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WindowsTopBar;

[JsonConverter(typeof(JsonStringEnumConverter<TopBarTheme>))]
internal enum TopBarTheme { System, Dark, Light }

internal sealed record AppearanceSettings
{
    public const string DefaultShortcutFolder = "Shortcuts";
    public const int DefaultUsageReminderFirstMinutes = 60;
    public const int DefaultUsageReminderSecondMinutes = 90;
    private const string DefaultQqOpenShortcut = "Ctrl+Alt+X";
    private const string DefaultWeChatOpenShortcut = "Ctrl+Alt+W";

    public TopBarTheme Theme { get; init; } = TopBarTheme.System;
    public bool AcrylicEnabled { get; init; } = true;
    public bool UsageReminderEnabled { get; init; } = true;

    [JsonConverter(typeof(ReminderMinutesConverter))]
    public int UsageReminderFirstMinutes { get; init; } = DefaultUsageReminderFirstMinutes;

    [JsonConverter(typeof(ReminderMinutesConverter))]
    public int UsageReminderSecondMinutes { get; init; } = DefaultUsageReminderSecondMinutes;

    public string? ShortcutFolder { get; init; } = DefaultShortcutFolder;

    [JsonConverter(typeof(ShortcutStringConverter))]
    public string? QqOpenShortcut { get; init; } = DefaultQqOpenShortcut;

    [JsonConverter(typeof(ShortcutStringConverter))]
    public string? WeChatOpenShortcut { get; init; } = DefaultWeChatOpenShortcut;

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsTopBar");

    public static string FilePath { get; } = Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppearanceSettings Load(string path)
    {
        try
        {
            using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var settings = JsonSerializer.Deserialize<AppearanceSettings>(file, JsonOptions);
            if (settings is null || !Enum.IsDefined(settings.Theme))
            {
                return new();
            }

            return settings.UsageReminderFirstMinutes > 0 && settings.UsageReminderSecondMinutes > settings.UsageReminderFirstMinutes
                ? settings
                : settings with
                {
                    UsageReminderFirstMinutes = DefaultUsageReminderFirstMinutes,
                    UsageReminderSecondMinutes = DefaultUsageReminderSecondMinutes
                };
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

    public static void PrepareForEditing(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                new AppearanceSettings().TrySave(path);
                return;
            }

            if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject settings)
            {
                var addedDefaults = settings.TryAdd(nameof(ShortcutFolder), DefaultShortcutFolder);
                addedDefaults |= settings.TryAdd(nameof(QqOpenShortcut), DefaultQqOpenShortcut);
                addedDefaults |= settings.TryAdd(nameof(WeChatOpenShortcut), DefaultWeChatOpenShortcut);
                addedDefaults |= settings.TryAdd(nameof(UsageReminderEnabled), true);
                addedDefaults |= settings.TryAdd(nameof(UsageReminderFirstMinutes), DefaultUsageReminderFirstMinutes);
                addedDefaults |= settings.TryAdd(nameof(UsageReminderSecondMinutes), DefaultUsageReminderSecondMinutes);
                if (addedDefaults)
                {
                    TryWrite(path, JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions));
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.TraceWarning("Could not prepare WindowsTopBar settings for editing: {0}", error.Message);
        }
    }

    public bool TrySave(string path) => TryWrite(path, JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions));

    private static bool TryWrite(string path, byte[] content)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(temporaryPath, content);
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

    private sealed class ReminderMinutesConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var minutes))
            {
                return minutes;
            }

            reader.Skip();
            return 0;
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    private sealed class ShortcutStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString();
            }

            // Ignore a malformed shortcut without discarding the other settings.
            reader.Skip();
            return null;
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }
}

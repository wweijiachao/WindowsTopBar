#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property Nullable=enable

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;

var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
var settings = assembly.GetType("WindowsTopBar.AppearanceSettings", throwOnError: true)!;
var shortcuts = assembly.GetType("WindowsTopBar.SystemShortcuts", throwOnError: true)!;
var parse = shortcuts.GetMethod("ParseShortcut", BindingFlags.NonPublic | BindingFlags.Static)!;

CheckKeys("Ctrl+Alt+X", [0x11, 0x12, 0x58]);
CheckKeys("Ctrl+Alt+W", [0x11, 0x12, 0x57]);
CheckKeys("  shift + ctrl + q  ", [0x11, 0x10, 0x51]);
CheckKeys("Alt+F8", [0x12, 0x77]);
CheckKeys("Ctrl+9", [0x11, 0x39]);
CheckKeys("Ctrl+Right", [0x11, 0x27]);
CheckKeys("F9", [0x78]);
foreach (var invalid in new[] { null, "", "Ctrl++Alt+X", "Ctrl+Shift+", "Opt+R", "Ctrl+LeftShift", "Ctrl+F55", "X" })
{
    Require(Parse(invalid) is null, $"Accepted invalid shortcut '{invalid}'.");
}

var folder = Directory.CreateTempSubdirectory("topbar-settings-");
var path = Path.Combine(folder.FullName, "settings.json");
var load = settings.GetMethod("Load")!;
var prepare = settings.GetMethod("PrepareForEditing")!;
try
{
    var defaults = load.Invoke(null, [path])!;
    Require(Value(defaults, "QqOpenShortcut") as string == "Ctrl+Alt+X", "Missing QQ default.");
    Require(Value(defaults, "WeChatOpenShortcut") as string == "Ctrl+Alt+W", "Missing WeChat default.");
    prepare.Invoke(null, [path]);
    Require((string?)ReadSettings()["QqOpenShortcut"] == "Ctrl+Alt+X", "New settings omitted the QQ shortcut.");
    Require((string?)ReadSettings()["WeChatOpenShortcut"] == "Ctrl+Alt+W", "New settings omitted the WeChat shortcut.");

    File.WriteAllText(path, """{"Theme":"Dark","ShortcutFolder":"Apps","QqOpenShortcut":"Ctrl+Shift+Q"}""");
    prepare.Invoke(null, [path]);
    var migrated = ReadSettings();
    Require((string?)migrated["Theme"] == "Dark" && (string?)migrated["ShortcutFolder"] == "Apps",
        "Migration changed existing settings.");
    Require((string?)migrated["QqOpenShortcut"] == "Ctrl+Shift+Q", "Migration overwrote the customized QQ shortcut.");
    Require((string?)migrated["WeChatOpenShortcut"] == "Ctrl+Alt+W", "Migration omitted the missing WeChat shortcut.");

    migrated["WeChatOpenShortcut"] = "Alt+F8";
    File.WriteAllText(path, migrated.ToJsonString());
    var updated = load.Invoke(null, [path])!;
    Require(Value(updated, "WeChatOpenShortcut") as string == "Alt+F8", "Did not reload the edited WeChat shortcut.");
    Require(settings.GetMethod("TrySave")!.Invoke(updated, [path]) is true, "Settings save failed.");
    var saved = ReadSettings();
    Require((string?)saved["QqOpenShortcut"] == "Ctrl+Shift+Q" && (string?)saved["WeChatOpenShortcut"] == "Alt+F8",
        "Save lost a customized shortcut.");

    File.WriteAllText(path, """{"Theme":"Dark","QqOpenShortcut":{"bad":true},"WeChatOpenShortcut":"Alt+F8"}""");
    var malformed = load.Invoke(null, [path])!;
    Require(Value(malformed, "QqOpenShortcut") is null, "Invalid QQ shortcut type was not ignored.");
    Require(Value(malformed, "WeChatOpenShortcut") as string == "Alt+F8" &&
        Value(malformed, "Theme")?.ToString() == "Dark", "Invalid QQ shortcut discarded other settings.");
}
finally
{
    File.Delete(path);
    folder.Delete();
}

Console.WriteLine("Settings and shortcut checks passed.");

ushort[]? Parse(string? shortcut) => (ushort[]?)parse.Invoke(null, [shortcut]);
JsonObject ReadSettings() => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
object? Value(object record, string name) => settings.GetProperty(name)!.GetValue(record);

void CheckKeys(string shortcut, ushort[] expected) =>
    Require(Parse(shortcut) is { } actual && actual.SequenceEqual(expected), $"Unexpected keys for '{shortcut}'.");

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property UseAppHost=false
#:property Nullable=enable

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Media;

// XML, geometry and temporary-directory checks only; no windows or desktop interaction.
var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
var entryType = assembly.GetType("WindowsTopBar.ShortcutEntry", true)!;
var loader = assembly.GetType("WindowsTopBar.ShortcutIcon", true)!.GetMethod("Load")!;
var catalogType = assembly.GetType("WindowsTopBar.PinnedShortcuts", true)!;
var folder = Directory.CreateTempSubdirectory("topbar-icons-");
using var catalog = (IDisposable)Activator.CreateInstance(catalogType)!;
try
{
    var pins = Directory.CreateDirectory(Path.Combine(folder.FullName, "Pinned")).FullName;
    var shortcut = Path.Combine(pins, "ChatGPT.url");
    var icon = Path.ChangeExtension(shortcut, ".icon.xaml");
    File.WriteAllText(shortcut, "");
    object Load() => loader.Invoke(null, [Activator.CreateInstance(entryType, [shortcut])!])!;
    Geometry? GeometryOf(object entry) => (Geometry?)entryType.GetProperty("IconGeometry")!.GetValue(entry);
    Check(GeometryOf(Load()) is null, "Missing sidecar did not fall back.");
    File.WriteAllText(icon, "<Path Data=\"M0,0 L24,0 24,24 0,24 Z\" />");
    var loaded = Load();
    Check(GeometryOf(loaded) is { IsFrozen: true } geometry && geometry.Bounds.Width == 24 && geometry.Bounds.Height == 24,
        "Valid path was not parsed/frozen.");
    foreach (var invalid in new[]
    {
        "<Path Data=\"broken\" />", "<Path Data=\"{Binding Bad}\" />", "<Path Data=\"M0,0\" />",
        "<Grid />", "<Path", "<Path><Path.Data /></Path>", "<Path xmlns=\"urn:other\" Data=\"M0,0 L1,1 Z\" />",
        "<!DOCTYPE Path [<!ENTITY d 'M0,0 L1,1 Z'>]><Path Data=\"&d;\" />",
        "<Path Data=\"" + new string(' ', 65536) + "\" />"
    })
    {
        File.WriteAllText(icon, invalid);
        Check(GeometryOf(Load()) is null, "Unsupported or malformed icon did not fall back.");
    }
    File.Copy(Path.GetFullPath(args.Length > 1 ? args[1] : "assets/shortcut-icons/ChatGPT.icon.xaml"), icon, overwrite: true);
    var chatgpt = GeometryOf(Load());
    Check(chatgpt is { IsFrozen: true } && chatgpt.Bounds.Width > 23 && chatgpt.Bounds.Height > 23 && chatgpt.GetArea() > 0,
        "ChatGPT example has invalid/empty geometry.");
    var first = await Read(force: true);
    Check(GeometryOf(Pin(first!)) is not null, "Catalog did not load the pinned icon.");
    await Task.Delay(100); await Read();
    Check(await Read() is null, "Unchanged directory was read again.");
    File.WriteAllText(icon, "<Path Data=\"M0,0 L8,0 8,8 Z\" />");
    await WaitFor(() => PinResult() is { } entry && GeometryOf(entry)?.Bounds.Width == 8);
    File.Delete(icon);
    await WaitFor(() => PinResult() is { } entry && GeometryOf(entry) is null);
    Console.WriteLine("Shortcut icon parsing, fallback, frozen geometry, ChatGPT asset, cache, change and removal checks passed.");

    async Task<object?> Read(bool force = false)
    {
        var task = (Task)catalogType.GetMethod("ReadAsync")!.Invoke(catalog, [force, folder.FullName])!;
        await task; return task.GetType().GetProperty("Result")!.GetValue(task);
    }
    object Pin(object snapshot) => ((Array)snapshot.GetType().GetProperty("Pinned")!.GetValue(snapshot)!).GetValue(0)!;
    object? PinResult()
    {
        var snapshot = Read().GetAwaiter().GetResult();
        return snapshot is null ? null : Pin(snapshot);
    }
    async Task WaitFor(Func<bool> ready)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!ready())
        {
            Check(Environment.TickCount64 < deadline, "Icon file event was not refreshed.");
            await Task.Delay(20);
        }
    }
}
finally { catalog.Dispose(); folder.Delete(true); }

void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }

#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property UseAppHost=false

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// Temporary files and catalog data only. No windows, process activation, or desktop interaction.
var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
var catalogType = assembly.GetType("WindowsTopBar.PinnedShortcuts", true)!;
var entryType = assembly.GetType("WindowsTopBar.ShortcutEntry", true)!;
var folder = Directory.CreateTempSubdirectory("topbar-order-");
using var catalog = (IDisposable)Activator.CreateInstance(catalogType)!;
try
{
    var pins = Directory.CreateDirectory(Path.Combine(folder.FullName, "Pinned")).FullName;
    foreach (var name in new[] { "alpha.url", "beta.url", "gamma.url" })
    {
        File.WriteAllText(Path.Combine(pins, name), "");
        File.WriteAllText(Path.Combine(folder.FullName, name), "");
    }
    var order = Path.Combine(pins, "order.json");
    Check(Names((await Read(true))!, "Pinned") == "alpha,beta,gamma", "Missing config changed filename order.");
    File.WriteAllText(order, "[\"GAMMA.URL\",\"missing.url\",\"gamma.url\",\"alpha.url\"]");
    var snapshot = (await Read(true))!;
    Check(Names(snapshot, "Pinned") == "gamma,alpha,beta", "Priority, case matching, duplicates or unlisted fallback failed.");
    Check(Names(snapshot, "Menu") == "alpha,beta,gamma", "Pinned configuration reordered the root menu.");
    foreach (var invalid in new[] { "broken", "{}", "[1]", "null", "[]" })
    {
        File.WriteAllText(order, invalid);
        Check(Names((await Read(true))!, "Pinned") == "alpha,beta,gamma", "Invalid/empty config did not fall back.");
    }
    File.WriteAllText(order, "[null,\"\",\"beta.url\"]");
    Check(Names((await Read(true))!, "Pinned") == "beta,alpha,gamma", "Null/empty names affected valid entries.");
    using (var locked = File.Open(order, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        Check(Names((await Read(true))!, "Pinned") == "alpha,beta,gamma", "Unreadable config did not fall back.");
    File.WriteAllText(order, "[\"gamma.url\",\"beta.url\",\"alpha.url\"]");
    await WaitFor("gamma,beta,alpha");
    File.Move(order, order + ".backup");
    await WaitFor("alpha,beta,gamma");
    File.Move(order + ".backup", order);
    await WaitFor("gamma,beta,alpha");
    File.Delete(order);
    await WaitFor("alpha,beta,gamma");
    await Task.Delay(100); await Read();
    Check(await Read() is null, "A clean directory was reread.");
    Console.WriteLine("Pinned custom order, fallback, duplicates, root isolation, watcher refresh/removal, and cache checks passed.");

    string Names(object snapshot, string property) => string.Join(",", ((Array)snapshot.GetType().GetProperty(property)!.GetValue(snapshot)!)
        .Cast<object>().Select(entry => (string)entryType.GetProperty("Name")!.GetValue(entry)!));
    async Task<object> Read(bool force = false)
    {
        var task = (Task)catalogType.GetMethod("ReadAsync")!.Invoke(catalog, [force, folder.FullName])!;
        await task; return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }
    async Task WaitFor(string expected)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (true)
        {
            var snapshot = await Read();
            if (snapshot is not null && Names(snapshot, "Pinned") == expected) return;
            Check(Environment.TickCount64 < deadline, "Sorting config file change was not refreshed.");
            await Task.Delay(20);
        }
    }
}
finally { catalog.Dispose(); folder.Delete(true); }

void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }

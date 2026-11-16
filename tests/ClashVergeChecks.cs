#:property TargetFramework=net10.0-windows
#:property PublishAot=false
#:property UseAppHost=false

using System;
using System.IO;
using System.Reflection;

var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
var settings = assembly.GetType("WindowsTopBar.ClashVergeSettings", throwOnError: true)!;
var readTooltip = settings.GetMethod("ReadTooltip")!;
var folder = Directory.CreateTempSubdirectory("WindowsTopBarClashChecks").FullName;
var vergePath = Path.Combine(folder, "verge.yaml");
var profilesPath = Path.Combine(folder, "profiles.yaml");
try
{
    Check(readTooltip.Invoke(null, [folder, "2.5.10"]) is null, "Missing settings should fall back to the app name.");

    File.WriteAllText(vergePath, "enable_system_proxy: false\nenable_tun_mode: true\n");
    File.WriteAllText(profilesPath, """
        current: selected
        items:
        - uid: other
          name: Other
        - uid: selected
          type: remote
          selected:
          - name: Nested group
          name: 'Main: today''s profile'
        """);
    Check(Equals(readTooltip.Invoke(null, [folder, "2.5.10"]),
        "Clash Verge 2.5.10\n系统代理: off\nTUN: on（配置）\n订阅: Main: today's profile"),
        "The tooltip must show the current profile and mark the configured TUN state.");

    File.Delete(profilesPath);
    Check(Equals(readTooltip.Invoke(null, [folder, null]), "Clash Verge\n系统代理: off\nTUN: on（配置）\n订阅: 未知"),
        "Missing profile data should preserve the available status.");
}
finally
{
    File.Delete(vergePath);
    File.Delete(profilesPath);
    Directory.Delete(folder);
}

Console.WriteLine("Clash Verge tooltip checks passed.");

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

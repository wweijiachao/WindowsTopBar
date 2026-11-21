#:property TargetFramework=net10.0-windows
#:property PublishAot=false
#:property UseAppHost=false
#:property Nullable=enable

using System;
using System.IO;
using System.Reflection;

// Pure checks with synthetic power states: no power APIs, device queries, or desktop interaction.
var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
var format = assembly.GetType("WindowsTopBar.SystemStatus", true)!
    .GetMethod("FormatBatteryRemainingTime", BindingFlags.Static | BindingFlags.NonPublic)!;

Check("hours and minutes", 0, 1, 12000, "3 小时 20 分钟");
Check("minutes only", 0, 2, 3540, "59 分钟");
Check("whole hour", 0, 1, 3600, "1 小时");
Check("seconds truncated", 0, 1, 3659, "1 小时");
Check("under a minute", 0, 4, 59, "不足 1 分钟");
Check("zero remaining", 0, 4, 0, "不足 1 分钟");
Check("over a day", 0, 1, 90060, "25 小时 1 分钟");
Check("unknown estimate", 0, 1, uint.MaxValue, null);
Check("plugged in, not charging", 1, 1, 12000, null);
Check("charging", 1, 9, 12000, null);
Check("charging flag during transition", 0, 8, 12000, null);
Check("unknown AC state", 255, 1, 12000, null);
Check("no battery", 0, 128, 12000, null);
Check("unknown battery flags", 0, 255, 12000, null);
Check("unclassified battery level", 0, 0, 600, "10 分钟");
Console.WriteLine("Battery remaining-time checks passed (15 cases).");

void Check(string name, byte ac, byte flags, uint seconds, string? expected)
{
    var actual = (string?)format.Invoke(null, [ac, flags, seconds]);
    if (actual != expected)
        throw new InvalidOperationException($"{name}: expected '{expected ?? "<none>"}', got '{actual ?? "<none>"}'.");
}

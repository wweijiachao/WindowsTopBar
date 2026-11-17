#:property TargetFramework=net10.0-windows
#:property PublishAot=false
#:property UseAppHost=false
#:property Nullable=enable

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

const long minute = 60_000;
var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
var model = assembly.GetType("WindowsTopBar.UsageReminder", throwOnError: true)!;
var interruption = assembly.GetType("WindowsTopBar.UsageInterruption", throwOnError: true)!;
var signals = assembly.GetType("WindowsTopBar.UsageSystemEvents", throwOnError: true)!;
var reminder = Activator.CreateInstance(model)!;

Check(Phase(0) == "Hidden", "The clock should wait for the user's reminder setting.");
SetEnabled(true, 0);
Check(Phase(60 * minute - 1) == "Normal", "First reminder started early.");
Check(Phase(60 * minute) == "FirstReminder" && Phase(90 * minute - 1) == "FirstReminder", "First threshold is wrong.");
Check(Phase(90 * minute) == "SecondReminder" && Phase(180 * minute) == "SecondReminder", "Second threshold should remain steady.");
Check(Phase(30 * minute, 30, 45) == "FirstReminder" && Phase(45 * minute, 30, 45) == "SecondReminder", "Custom thresholds were ignored.");

Call("StartRest", 10_000L);
Check(Phase(180 * minute) == "Resting", "Starting a rest should pause the reminder.");
Check(Call("ShouldCheckInput", 14_999L) is false && Call("ShouldCheckInput", 15_000L) is true, "Wrong input grace period.");
Call("ObserveInput", 14_999L, 180 * minute);
Check(Phase(180 * minute) == "Resting", "Input during the grace period resumed the clock.");
Call("ObserveInput", 15_000L, 180 * minute);
Check(Phase(180 * minute) == "Normal" && Call("ElapsedMinutes", 180 * minute) is 0L, "New input must restart at zero.");

SetEnabled(false, 200 * minute);
Check(Phase(200 * minute) == "Hidden" && Call("ShouldCheckInput", 200 * minute) is false, "Disabled reminders should end the session.");
SetEnabled(true, 201 * minute);
Check(Phase(201 * minute) == "Normal" && Call("ElapsedMinutes", 201 * minute) is 0L, "Re-enabling should start a fresh timer.");

Interrupt("Locked", true, 250 * minute, 250 * minute);
Interrupt("DisplayOff", true, 251 * minute, 251 * minute);
Interrupt("Suspended", true, 252 * minute, 252 * minute);
Check(Phase(252 * minute) == "Hidden", "An interrupted session must hide the reminder.");
Interrupt("Suspended", false, 252 * minute, 400 * minute);
Interrupt("Locked", false, 252 * minute, 400 * minute);
Check(Phase(252 * minute) == "Hidden", "Resume and unlock must not restart while the display is off.");
Interrupt("DisplayOff", false, 253 * minute, 401 * minute);
Check(Phase(253 * minute) == "Normal" && Call("ElapsedMinutes", 253 * minute) is 0L, "Returning to the desktop must reset elapsed usage.");
Interrupt("DisplayOff", false, 254 * minute, 402 * minute);
Check(Call("ElapsedMinutes", 254 * minute) is 1L, "A duplicate display-on notification reset the session.");

Call("StartRest", 403 * minute);
Interrupt("Locked", true, 256 * minute, 404 * minute);
Call("ObserveInput", 404 * minute, 256 * minute);
Interrupt("Locked", false, 258 * minute, 406 * minute);
Call("ObserveInput", 406 * minute, 258 * minute);
Check(Phase(258 * minute) == "Resting", "Input while locked or during unlock ended the rest.");
Call("ObserveInput", 406 * minute + 1, 259 * minute);
Check(Phase(259 * minute) == "Normal", "New input after returning did not restart the clock.");

var readInterruption = signals.GetMethod("ReadInterruption")!;
var data = Marshal.AllocHGlobal(24);
try
{
    Marshal.StructureToPtr(new Guid("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5"), data, false);
    Marshal.WriteInt32(data, 16, 4);
    Marshal.WriteInt32(data, 20, 0);
    Check(ReadInterruption(0x0218, 0x8013, data) == "(DisplayOff, True)", "Display-off message was not decoded.");
    Marshal.WriteInt32(data, 20, 1);
    Check(ReadInterruption(0x0218, 0x8013, data) == "(DisplayOff, False)", "Display-on message was not decoded.");
    Marshal.WriteInt32(data, 20, 2);
    Check(ReadInterruption(0x0218, 0x8013, data) == "(DisplayOff, False)", "Dimming should not end a session.");
    Marshal.WriteInt32(data, 16, 3);
    Check(ReadInterruption(0x0218, 0x8013, data) is null, "An invalid power payload was accepted.");
    Check(ReadInterruption(0x02B1, 7, 0) == "(Locked, True)" && ReadInterruption(0x02B1, 8, 0) == "(Locked, False)",
        "Lock and unlock messages were not decoded.");
    Check(ReadInterruption(0x0218, 4, 0) == "(Suspended, True)" && ReadInterruption(0x0218, 0x12, 0) == "(Suspended, False)",
        "Suspend and wake messages were not decoded.");
}
finally
{
    Marshal.FreeHGlobal(data);
}

Console.WriteLine("Usage reminder checks passed.");

object? Call(string method, params object[] values) => model.GetMethod(method)!.Invoke(reminder, values);
string? Phase(long now, int first = 60, int second = 90) => Call("GetPhase", now, first, second)?.ToString();
void SetEnabled(bool enabled, long now) => Call("SetEnabled", enabled, now);
void Interrupt(string reason, bool state, long awake, long uptime) =>
    Call("SetInterrupted", Enum.Parse(interruption, reason), state, awake, uptime);
string? ReadInterruption(int message, nint kind, nint payload) => readInterruption.Invoke(null, [message, kind, payload])?.ToString();

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

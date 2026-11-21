#:property TargetFramework=net10.0-windows
#:property PublishAot=false
#:property UseAppHost=false

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

// ABI layout checks only: no audio APIs, device queries, windows, or desktop interaction.
var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
var audio = assembly.GetType("WindowsTopBar.AudioStatus", true)!;
var variant = audio.GetNestedType("PropVariant", BindingFlags.NonPublic)!;
var key = audio.GetNestedType("PropertyKey", BindingFlags.NonPublic)!;

Check("PROPVARIANT size", Marshal.SizeOf(variant), IntPtr.Size == 8 ? 24 : 16);
Check("PROPVARIANT type offset", Marshal.OffsetOf(variant, "Type").ToInt32(), 0);
Check("PROPVARIANT string offset", Marshal.OffsetOf(variant, "StringPointer").ToInt32(), 8);
Check("PROPERTYKEY size", Marshal.SizeOf(key), 20);
Check("PROPERTYKEY property ID offset", Marshal.OffsetOf(key, "PropertyId").ToInt32(), 16);

Console.WriteLine("Audio interop ABI layout checks passed.");

static void Check(string name, int actual, int expected)
{
    if (actual != expected)
        throw new InvalidOperationException($"{name}: expected {expected}, got {actual}.");
}

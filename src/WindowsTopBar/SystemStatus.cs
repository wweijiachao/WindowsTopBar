using System.Globalization;
using System.Runtime.InteropServices;

namespace WindowsTopBar;

internal static class SystemStatus
{
    public static (string Label, string Name) ReadInputLanguage()
    {
        var foreground = GetForegroundWindow();
        var thread = foreground == 0 ? 0 : GetWindowThreadProcessId(foreground, out _);
        var layout = GetKeyboardLayout(thread);
        var languageId = (int)(layout & 0xFFFF);
        if (languageId == 0)
        {
            return ("—", "输入语言未知");
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(languageId);
            var label = culture.TwoLetterISOLanguageName switch
            {
                "zh" => "中",
                "ja" => "日",
                "ko" => "한",
                var code => code.ToUpperInvariant()
            };
            return (label, culture.NativeName);
        }
        catch (CultureNotFoundException)
        {
            return ("—", "输入语言未知");
        }
    }

    public static (int? Percentage, bool Charging, bool HasBattery) ReadBattery()
    {
        if (!GetSystemPowerStatus(out var status) || status.BatteryFlags == 255)
        {
            return (null, false, true);
        }

        return (status.Percentage <= 100 ? status.Percentage : null,
            (status.BatteryFlags & 8) != 0, (status.BatteryFlags & 128) == 0);
    }

    public static bool? ReadWifiConnected()
    {
        if (WlanOpenHandle(2, 0, out _, out var client) != 0)
        {
            return null;
        }

        try
        {
            if (WlanEnumInterfaces(client, 0, out var list) != 0)
            {
                return null;
            }

            try
            {
                var count = Marshal.ReadInt32(list);
                var size = Marshal.SizeOf<WlanInterface>();
                for (var index = 0; index < count; index++)
                {
                    var adapter = Marshal.PtrToStructure<WlanInterface>(list + 8 + index * size);
                    if (adapter.State == 1) // wlan_interface_state_connected
                    {
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                WlanFreeMemory(list);
            }
        }
        finally
        {
            WlanCloseHandle(client, 0);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte AcLineStatus, BatteryFlags, Percentage, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanInterface
    {
        public Guid Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Description;
        public int State;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern bool GetSystemPowerStatus(out PowerStatus status);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetKeyboardLayout(uint threadId);

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanOpenHandle(uint version, nint reserved, out uint negotiatedVersion, out nint client);

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanEnumInterfaces(nint client, nint reserved, out nint list);

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern void WlanFreeMemory(nint memory);

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanCloseHandle(nint client, nint reserved);
}

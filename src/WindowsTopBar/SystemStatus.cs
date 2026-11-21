using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowsTopBar;

internal static class SystemStatus
{
    private const uint WmImeControl = 0x0283;
    private const uint GetConversionMode = 1;
    private const uint GetOpenStatus = 5;
    private const uint AbortIfHung = 2;
    private const uint ImeQueryTimeout = 80;
    private const uint NativeConversion = 1;

    public static (string Label, string Name)? ReadInputLanguage()
    {
        var foreground = GetForegroundWindow();
        var thread = foreground == 0 ? 0 : GetWindowThreadProcessId(foreground, out _);
        var layout = GetKeyboardLayout(thread);
        var languageId = (int)(layout & 0xFFFF);
        if (languageId == 0)
        {
            return null;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(languageId);
            if (culture.TwoLetterISOLanguageName == "zh")
            {
                return ReadChineseMode(foreground, thread) switch
                {
                    true => ("中", "中文输入模式"),
                    false => ("英", "英文输入模式"),
                    null => null
                };
            }

            var label = culture.TwoLetterISOLanguageName switch
            {
                "en" => "英",
                "ja" => "日",
                "ko" => "한",
                var code => code.ToUpperInvariant()
            };
            return (label, culture.NativeName);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static bool? ReadChineseMode(nint foreground, uint thread)
    {
        if (foreground == 0 || thread == 0)
        {
            return null;
        }

        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        var target = GetGUIThreadInfo(thread, ref info) && info.Focus != 0 ? info.Focus : foreground;
        var ime = ImmGetDefaultIMEWnd(target);
        if (ime == 0 || !TryReadIme(ime, GetOpenStatus, out var open) || open > 1)
        {
            return null;
        }

        if (open == 0)
        {
            return false;
        }

        return TryReadIme(ime, GetConversionMode, out var conversion) ? (conversion & NativeConversion) != 0 : null;
    }

    private static bool TryReadIme(nint ime, uint command, out nuint value) =>
        SendMessageTimeoutW(ime, WmImeControl, command, 0, AbortIfHung, ImeQueryTimeout, out value) != 0;

    public static (int? Percentage, bool Charging, bool HasBattery, string? RemainingTime) ReadBattery()
    {
        if (!GetSystemPowerStatus(out var status) || status.BatteryFlags == 255)
        {
            return (null, false, true, null);
        }

        return (status.Percentage <= 100 ? status.Percentage : null,
            (status.BatteryFlags & 8) != 0, (status.BatteryFlags & 128) == 0,
            FormatBatteryRemainingTime(status.AcLineStatus, status.BatteryFlags, status.BatteryLifeTime));
    }

    private static string? FormatBatteryRemainingTime(byte acLineStatus, byte batteryFlags, uint seconds)
    {
        // Only show estimates when explicitly on battery; ignore stale values on AC or unknown power state.
        if (acLineStatus != 0 || batteryFlags == 255 || (batteryFlags & (8 | 128)) != 0 || seconds == uint.MaxValue)
        {
            return null;
        }

        var hours = seconds / 3600;
        var minutes = seconds / 60 % 60;
        return (hours, minutes) switch
        {
            (0, 0) => "不足 1 分钟",
            (0, _) => $"{minutes} 分钟",
            (_, 0) => $"{hours} 小时",
            _ => $"{hours} 小时 {minutes} 分钟"
        };
    }

    public static (bool? Connected, string? Ssid) ReadWifi()
    {
        if (WlanOpenHandle(2, 0, out _, out var client) != 0)
        {
            return (null, null);
        }

        try
        {
            if (WlanEnumInterfaces(client, 0, out var list) != 0)
            {
                return (null, null);
            }

            try
            {
                var count = Marshal.ReadInt32(list);
                var size = Marshal.SizeOf<WlanInterface>();
                var connected = false;
                for (var index = 0; index < count; index++)
                {
                    var adapter = Marshal.PtrToStructure<WlanInterface>(list + 8 + index * size);
                    if (adapter.State == 1) // wlan_interface_state_connected
                    {
                        connected = true;
                        var ssid = ReadConnectedSsid(client, adapter.Id);
                        if (ssid is not null)
                        {
                            return (true, ssid);
                        }
                    }
                }

                return (connected, null);
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

    private static string? ReadConnectedSsid(nint client, Guid adapter)
    {
        const uint currentConnection = 7;
        if (WlanQueryInterface(client, in adapter, currentConnection, 0, out var size, out var data, 0) != 0)
        {
            return null;
        }

        try
        {
            if (data == 0 || size < Marshal.SizeOf<WlanConnectionSsid>())
            {
                return null;
            }

            var connection = Marshal.PtrToStructure<WlanConnectionSsid>(data);
            return connection.SsidLength is > 0 and <= 32
                ? Encoding.UTF8.GetString(connection.Ssid, 0, (int)connection.SsidLength)
                : null;
        }
        finally
        {
            WlanFreeMemory(data);
        }
    }

    // Prefix of WLAN_CONNECTION_ATTRIBUTES through the association's DOT11_SSID.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanConnectionSsid
    {
        public int State, ConnectionMode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ProfileName;
        public uint SsidLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Ssid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte AcLineStatus, BatteryFlags, Percentage, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int CaretLeft, CaretTop, CaretRight, CaretBottom;
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

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("imm32.dll", ExactSpelling = true)]
    private static extern nint ImmGetDefaultIMEWnd(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint SendMessageTimeoutW(nint window, uint message, nuint command, nint parameter,
        uint flags, uint timeout, out nuint result);

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanOpenHandle(uint version, nint reserved, out uint negotiatedVersion, out nint client);

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanEnumInterfaces(nint client, nint reserved, out nint list);

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanQueryInterface(nint client, in Guid adapter, uint operation, nint reserved,
        out uint size, out nint data, nint opcodeType);

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern void WlanFreeMemory(nint memory);

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanCloseHandle(nint client, nint reserved);
}

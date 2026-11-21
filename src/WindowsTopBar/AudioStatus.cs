using System.Runtime.InteropServices;

namespace WindowsTopBar;

internal static class AudioStatus
{
    private static readonly PropertyKey DeviceFriendlyName = new()
    {
        FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        PropertyId = 14
    };

    public static (bool Muted, int Percentage, string? DeviceName)? Read()
    {
        DeviceEnumerator? enumerator = null;
        AudioDevice? device = null;
        object? endpoint = null;
        try
        {
            enumerator = (DeviceEnumerator)new DeviceEnumeratorClass();
            enumerator.GetDefaultAudioEndpoint(0, 1, out device); // Render, multimedia
            var id = typeof(EndpointVolume).GUID;
            device.Activate(in id, 23, 0, out endpoint); // CLSCTX_ALL
            var volume = (EndpointVolume)endpoint;
            volume.GetMute(out var muted);
            volume.GetMasterVolumeLevelScalar(out var level);
            return (muted, (int)Math.Round(Math.Clamp(level, 0, 1) * 100), ReadDeviceName(device));
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (endpoint is not null) Marshal.ReleaseComObject(endpoint);
            if (device is not null) Marshal.ReleaseComObject(device);
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }
    }

    private static string? ReadDeviceName(AudioDevice device)
    {
        PropertyStore? properties = null;
        try
        {
            device.OpenPropertyStore(0, out properties); // STGM_READ
            PropVariant value = default;
            try
            {
                properties.GetValue(in DeviceFriendlyName, out value);
                return value.Type == 31 // VT_LPWSTR
                    ? Marshal.PtrToStringUni(value.StringPointer)
                    : null;
            }
            finally
            {
                Marshal.ThrowExceptionForHR(PropVariantClear(ref value));
            }
        }
        catch (COMException)
        {
            // A missing or unreadable name must not discard the volume reading.
            return null;
        }
        finally
        {
            if (properties is not null) Marshal.ReleaseComObject(properties);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public nint StringPointer;
        // The native union also contains counted arrays: 16 bytes on x86, 24 on x64/ARM64.
        [FieldOffset(8)] public CountedPointer Array;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CountedPointer
    {
        public uint Count;
        public nint Pointer;
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int PropVariantClear(ref PropVariant value);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class DeviceEnumeratorClass { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface DeviceEnumerator
    {
        void EnumAudioEndpoints(int flow, uint stateMask, out nint devices);
        void GetDefaultAudioEndpoint(int flow, int role, out AudioDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface AudioDevice
    {
        void Activate(in Guid id, uint context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
        void OpenPropertyStore(uint access, out PropertyStore properties);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface PropertyStore
    {
        // Declaration order matches IPropertyStore's native COM vtable.
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(in PropertyKey key, out PropVariant value);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface EndpointVolume
    {
        // Declaration order matches the native COM vtable.
        void RegisterControlChangeNotify(nint callback);
        void UnregisterControlChangeNotify(nint callback);
        void GetChannelCount(out uint channels);
        void SetMasterVolumeLevel(float level, nint context);
        void SetMasterVolumeLevelScalar(float level, nint context);
        void GetMasterVolumeLevel(out float level);
        void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(uint channel, float level, nint context);
        void SetChannelVolumeLevelScalar(uint channel, float level, nint context);
        void GetChannelVolumeLevel(uint channel, out float level);
        void GetChannelVolumeLevelScalar(uint channel, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, nint context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}

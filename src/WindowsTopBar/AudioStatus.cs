using System.Runtime.InteropServices;

namespace WindowsTopBar;

internal static class AudioStatus
{
    public static (bool Muted, int Percentage)? Read()
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
            return (muted, (int)Math.Round(Math.Clamp(level, 0, 1) * 100));
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

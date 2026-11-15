using System.Runtime.InteropServices;

namespace Azuria.Shell.Native;

/// <summary>
/// Volume et coupure du son (sortie par défaut) et du micro (entrée par défaut), par l'API Core Audio
/// de Windows (<c>IAudioEndpointVolume</c>), celle des réglages du son. Rien d'autre n'est touché.
/// </summary>
internal static class Audio
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out nint devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, nint activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(nint notify);
        [PreserveSig] int UnregisterControlChangeNotify(nint notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float db);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float db, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float db);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    private const int eRender = 0, eCapture = 1, eConsole = 0, CLSCTX_ALL = 23;

    /// <summary>Sortie par défaut : niveau (0–100) et coupure ; null s'il n'y en a pas.</summary>
    public static (int Level, bool Muted)? Output => Read(eRender);

    /// <summary>Micro par défaut : niveau et coupure ; null s'il n'y en a pas.</summary>
    public static (int Level, bool Muted)? Input => Read(eCapture);

    public static void SetOutputLevel(int level) => With(eRender, v =>
    {
        var g = Guid.Empty;
        v.SetMasterVolumeLevelScalar(Math.Clamp(level, 0, 100) / 100f, ref g);
        if (level > 0) v.SetMute(false, ref g); // monter le son le rétablit, comme Windows
    });

    public static void SetOutputMuted(bool muted) => With(eRender, v => { var g = Guid.Empty; v.SetMute(muted, ref g); });

    public static void SetInputMuted(bool muted) => With(eCapture, v => { var g = Guid.Empty; v.SetMute(muted, ref g); });

    private static (int, bool)? Read(int flow)
    {
        (int, bool)? result = null;
        With(flow, v =>
        {
            if (v.GetMasterVolumeLevelScalar(out float level) == 0 && v.GetMute(out bool muted) == 0)
                result = ((int)Math.Round(level * 100), muted);
        });
        return result;
    }

    private static void With(int flow, Action<IAudioEndpointVolume> action)
    {
        object? enumerator = null, device = null, volume = null;
        try
        {
            enumerator = new MMDeviceEnumerator();
            if (((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(flow, eConsole, out var dev) != 0) return;
            device = dev;
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (dev.Activate(ref iid, CLSCTX_ALL, 0, out volume) != 0) return;
            action((IAudioEndpointVolume)volume);
        }
        catch { /* pas de périphérique audio */ }
        finally
        {
            foreach (var o in new[] { volume, device, enumerator })
                if (o is not null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
        }
    }
}

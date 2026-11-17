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
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Fmtid; public uint Pid; }
    [StructLayout(LayoutKind.Explicit)] private struct PropVariant { [FieldOffset(0)] public ushort Vt; [FieldOffset(8)] public nint Ptr; [FieldOffset(16)] public nint Pad; }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant v);

    // Choix de la sortie par défaut : l'interface IPolicyConfig des réglages du son de Windows. Elle
    // n'est pas documentée mais stable depuis Windows 7 (le mélangeur EarTrumpet s'en sert) ; l'appel
    // reste dans notre processus et passe par le service audio de Windows.
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] private class PolicyConfigClient { }
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(nint a, nint b);
        [PreserveSig] int GetDeviceFormat(nint a, int b, nint c);
        [PreserveSig] int ResetDeviceFormat(nint a);
        [PreserveSig] int SetDeviceFormat(nint a, nint b, nint c);
        [PreserveSig] int GetProcessingPeriod(nint a, int b, nint c, nint d);
        [PreserveSig] int SetProcessingPeriod(nint a, nint b);
        [PreserveSig] int GetShareMode(nint a, nint b);
        [PreserveSig] int SetShareMode(nint a, nint b);
        [PreserveSig] int GetPropertyValue(nint a, nint b, nint c);
        [PreserveSig] int SetPropertyValue(nint a, nint b, nint c);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    }

    private const int DEVICE_STATE_ACTIVE = 1, eMultimedia = 1, eCommunications = 2;
    private static PropertyKey FriendlyName = new()
    { Fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Pid = 14 };

    /// <summary>Sorties audio actives : identifiant, nom, et si c'est la sortie par défaut.</summary>
    public static List<(string Id, string Name, bool Default)> Outputs()
    {
        var list = new List<(string, string, bool)>();
        object? enumerator = null;
        try
        {
            enumerator = new MMDeviceEnumerator();
            var e = (IMMDeviceEnumerator)enumerator;
            string? def = null;
            if (e.GetDefaultAudioEndpoint(eRender, eConsole, out var d) == 0) { d.GetId(out def); Marshal.ReleaseComObject(d); }
            if (e.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, out nint pc) != 0 || pc == 0) return list;
            var coll = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(pc);
            Marshal.Release(pc);
            coll.GetCount(out uint n);
            for (uint i = 0; i < n; i++)
            {
                if (coll.Item(i, out var dev) != 0) continue;
                try
                {
                    dev.GetId(out string id);
                    string name = id;
                    if (dev.OpenPropertyStore(0 /* STGM_READ */, out var store) == 0)
                    {
                        if (store.GetValue(ref FriendlyName, out var v) == 0)
                        {
                            if (v.Vt == 31 /* VT_LPWSTR */) name = Marshal.PtrToStringUni(v.Ptr) ?? id;
                            PropVariantClear(ref v);
                        }
                        Marshal.ReleaseComObject(store);
                    }
                    list.Add((id, name, id == def));
                }
                finally { Marshal.ReleaseComObject(dev); }
            }
            Marshal.ReleaseComObject(coll);
        }
        catch { }
        finally { if (enumerator is not null) Marshal.ReleaseComObject(enumerator); }
        return list;
    }

    /// <summary>Fait de cette sortie la sortie par défaut (tous rôles, comme les réglages du son).</summary>
    public static void SetDefaultOutput(string id)
    {
        object? policy = null;
        try
        {
            policy = new PolicyConfigClient();
            var p = (IPolicyConfig)policy;
            foreach (int role in new[] { eConsole, eMultimedia, eCommunications }) p.SetDefaultEndpoint(id, role);
        }
        catch { }
        finally { if (policy is not null) Marshal.ReleaseComObject(policy); }
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

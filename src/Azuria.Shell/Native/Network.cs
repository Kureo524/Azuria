using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Azuria.Shell.Native;

/// <summary>Connexion actuelle : type, nom du réseau Wi-Fi et qualité du signal.</summary>
internal sealed record NetState(string Kind, string Name, int Signal);

/// <summary>
/// État du réseau, en lecture seule : cartes actives (<c>NetworkInterface</c>) et, pour le Wi-Fi, le
/// réseau en cours par l'API WLAN de Windows (nom et signal ; Windows peut les masquer si la
/// localisation est coupée).
/// </summary>
internal static class Network
{
    [DllImport("wlanapi.dll")] private static extern int WlanOpenHandle(uint version, nint reserved, out uint negotiated, out nint handle);
    [DllImport("wlanapi.dll")] private static extern int WlanCloseHandle(nint handle, nint reserved);
    [DllImport("wlanapi.dll")] private static extern int WlanEnumInterfaces(nint handle, nint reserved, out nint list);
    [DllImport("wlanapi.dll")] private static extern int WlanQueryInterface(nint handle, ref Guid iface, int opcode, nint reserved, out int size, out nint data, out int type);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(nint memory);
    private const int wlan_intf_opcode_current_connection = 7;
    private const int wlan_interface_state_connected = 1;

    /// <summary>« wifi », « ethernet » ou « none ».</summary>
    public static NetState Current()
    {
        bool wifi = false, ethernet = false;
        try
        {
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up) continue;
                // Une carte qui mène quelque part a une passerelle (les cartes virtuelles, en général non).
                if (!n.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any))) continue;
                if (n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) wifi = true;
                else if (n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet) ethernet = true;
            }
        }
        catch { }
        if (ethernet) return new NetState("ethernet", "Ethernet", 100);
        if (!wifi) return new NetState("none", "", 0);
        var (name, signal) = Wifi();
        return new NetState("wifi", name, signal);
    }

    // Réseau Wi-Fi en cours : WLAN_CONNECTION_ATTRIBUTES (SSID à l'octet 520, signal à l'octet 576).
    private static (string, int) Wifi()
    {
        if (WlanOpenHandle(2, 0, out _, out nint h) != 0) return ("Wi-Fi", 100);
        try
        {
            if (WlanEnumInterfaces(h, 0, out nint list) != 0) return ("Wi-Fi", 100);
            try
            {
                int count = Marshal.ReadInt32(list);
                for (int i = 0; i < count; i++)
                {
                    nint info = list + 8 + i * 532;                // WLAN_INTERFACE_INFO : GUID + nom (512) + état
                    var guid = Marshal.PtrToStructure<Guid>(info);
                    if (Marshal.ReadInt32(info + 528) != wlan_interface_state_connected) continue;
                    if (WlanQueryInterface(h, ref guid, wlan_intf_opcode_current_connection, 0, out _, out nint data, out _) != 0) continue;
                    try
                    {
                        int len = Math.Clamp(Marshal.ReadInt32(data + 520), 0, 32);
                        var ssid = new byte[len];
                        Marshal.Copy(data + 524, ssid, 0, len);
                        int signal = Marshal.ReadInt32(data + 576);
                        string name = System.Text.Encoding.UTF8.GetString(ssid);
                        return (name.Length > 0 ? name : "Wi-Fi", Math.Clamp(signal, 0, 100));
                    }
                    finally { WlanFreeMemory(data); }
                }
            }
            finally { WlanFreeMemory(list); }
        }
        catch { }
        finally { WlanCloseHandle(h, 0); }
        return ("Wi-Fi", 100);
    }
}

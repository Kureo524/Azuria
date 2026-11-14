using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Azuria.Shell.Native;

/// <summary>Une mesure des ressources, telle que l'onglet Performances l'affiche.</summary>
internal sealed record Stats(
    double Cpu, double? Gpu, double MemUsed, double MemTotal, string Disk, double DiskUsed, double DiskTotal,
    double NetDown, double NetUp, int? Battery, bool Charging, long Uptime);

/// <summary>
/// Les ressources de la machine, lues par les compteurs publics de Windows : temps processeur
/// (<c>GetSystemTimes</c>), mémoire (<c>GlobalMemoryStatusEx</c>), carte graphique (compteurs de
/// performance « GPU Engine », comme le Gestionnaire des tâches), disque système, débit réseau,
/// batterie. Lecture seule ; aucun processus n'est ouvert ni lu (CLAUDE.md).
/// </summary>
internal sealed class SystemStats : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE_ITEM { public nint Name; public uint Status; public uint Pad; public double Value; }

    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhOpenQuery(string? source, nint user, out nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhAddEnglishCounter(nint query, string path, nint user, out nint counter);
    [DllImport("pdh.dll")] private static extern int PdhCollectQueryData(nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhGetFormattedCounterArray(nint counter, uint format, ref uint size, out uint count, nint buffer);
    [DllImport("pdh.dll")] private static extern int PdhCloseQuery(nint query);
    private const uint PDH_FMT_DOUBLE = 0x00000200, PDH_FMT_NOCAP100 = 0x00008000;
    private const int PDH_MORE_DATA = unchecked((int)0x800007D2);

    private long _idle, _kernel, _user;
    private long _rx, _tx;
    private DateTime _netAt;
    private nint _gpuQuery, _gpuCounter;

    public SystemStats()
    {
        GetSystemTimes(out _idle, out _kernel, out _user);
        (_rx, _tx) = NetBytes();
        _netAt = DateTime.UtcNow;
        // Charge de la carte graphique : somme des moteurs « 3D » de tous les processus, comme le
        // Gestionnaire des tâches. Absent (machine virtuelle, pilote ancien) : on n'affiche rien.
        if (PdhOpenQuery(null, 0, out _gpuQuery) == 0 &&
            PdhAddEnglishCounter(_gpuQuery, @"\GPU Engine(*engtype_3D)\Utilization Percentage", 0, out _gpuCounter) == 0)
            PdhCollectQueryData(_gpuQuery);
        else _gpuCounter = 0;
    }

    /// <summary>Une mesure, depuis la précédente (à appeler environ chaque seconde).</summary>
    public Stats Sample()
    {
        // Processeur : part du temps non inactif depuis la dernière mesure.
        GetSystemTimes(out long idle, out long kernel, out long user);
        long total = (kernel - _kernel) + (user - _user);  // le temps noyau inclut l'inactif
        double cpu = total > 0 ? 100.0 * (1 - (double)(idle - _idle) / total) : 0;
        (_idle, _kernel, _user) = (idle, kernel, user);

        var mem = new MEMORYSTATUSEX { Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref mem);

        string root = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        double diskUsed = 0, diskTotal = 0;
        try
        {
            var d = new DriveInfo(root);
            diskTotal = d.TotalSize;
            diskUsed = d.TotalSize - d.TotalFreeSpace;
        }
        catch { }

        var (rx, tx) = NetBytes();
        var now = DateTime.UtcNow;
        double dt = Math.Max(0.001, (now - _netAt).TotalSeconds);
        double down = Math.Max(0, (rx - _rx) / dt), up = Math.Max(0, (tx - _tx) / dt);
        (_rx, _tx, _netAt) = (rx, tx, now);

        var (battery, charging) = Battery();

        return new Stats(Math.Clamp(cpu, 0, 100), Gpu(), mem.TotalPhys - mem.AvailPhys, mem.TotalPhys,
            root.TrimEnd('\\'), diskUsed, diskTotal, down, up, battery, charging, Environment.TickCount64 / 1000);
    }

    /// <summary>Batterie : pourcentage et charge en cours ; null sans batterie (PC fixe).</summary>
    public static (int? Percent, bool Charging) Battery()
    {
        if (GetSystemPowerStatus(out var p) && (p.BatteryFlag & 128) == 0 && p.BatteryLifePercent <= 100)
            return (p.BatteryLifePercent, p.ACLineStatus == 1);
        return (null, false);
    }

    private double? Gpu()
    {
        if (_gpuCounter == 0 || PdhCollectQueryData(_gpuQuery) != 0) return null;
        uint size = 0;
        int hr = PdhGetFormattedCounterArray(_gpuCounter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out uint count, 0);
        if (hr != PDH_MORE_DATA || size == 0) return 0;
        nint buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(_gpuCounter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, buf) != 0) return null;
            double sum = 0;
            int stride = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(buf + i * stride);
                if (item.Status == 0) sum += item.Value;
            }
            return Math.Clamp(sum, 0, 100);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    // Octets reçus et envoyés par les cartes réseau actives (hors boucle locale et tunnels).
    private static (long, long) NetBytes()
    {
        long rx = 0, tx = 0;
        try
        {
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up) continue;
                if (n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var s = n.GetIPStatistics();
                rx += s.BytesReceived;
                tx += s.BytesSent;
            }
        }
        catch { }
        return (rx, tx);
    }

    public void Dispose()
    {
        if (_gpuQuery != 0) PdhCloseQuery(_gpuQuery);
        _gpuQuery = 0;
    }
}

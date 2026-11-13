using System.Runtime.InteropServices;

namespace Azuria.Shell.Native;

/// <summary>Infos d'écran lues en direct (DPI, zone de travail), sans cache.</summary>
internal static class Monitors
{
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint hmon, ref MONITORINFO mi);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint hmon, int type, out uint x, out uint y);

    private static nint Handle(Rectangle bounds) => MonitorFromPoint(
        new POINT { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height / 2 },
        2 /* MONITOR_DEFAULTTONEAREST */);

    /// <summary>DPI effectif de l'écran (nécessite PerMonitorV2, forcé dans Program).</summary>
    public static uint Dpi(Screen s) =>
        GetDpiForMonitor(Handle(s.Bounds), 0 /* MDT_EFFECTIVE_DPI */, out uint d, out _) == 0 && d > 0 ? d : 96;

    /// <summary>Taille logique (px à 100 %) → pixels physiques pour cet écran.</summary>
    public static int Scale(Screen s, int logical) => (int)Math.Round(logical * Dpi(s) / 96.0);

    /// <summary>Zone de travail actuelle (hors barre des tâches et hors cadre d'Azuria).</summary>
    public static Rectangle WorkArea(Screen s)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(Handle(s.Bounds), ref mi)) return s.WorkingArea;
        return Rectangle.FromLTRB(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom);
    }
}

using System.Runtime.InteropServices;

namespace Azuria.Shell;

internal static class Program
{
    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(nint value);
    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
    private static readonly nint PerMonitorV2 = -4;

    [STAThread]
    private static void Main()
    {
        // Forcer PerMonitorV2 AVANT toute fenêtre : sans ça, le process reste en PerMonitor v1 et
        // le DPI vu de l'intérieur reste faux (fenêtre mal dimensionnée sur un écran mis à l'échelle).
        SetProcessDpiAwarenessContext(PerMonitorV2);

        ApplicationConfiguration.Initialize();

        // Squelette : pour l'instant, seule la barre. Les panneaux, le tableau de bord, etc.
        // viendront comme d'autres GlassWindow (DA §9).
        Application.Run(new BarWindow());
    }
}

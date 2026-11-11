namespace Azuria.Shell;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // ApplicationConfiguration applique le DPI PerMonitorV2 (propriété ApplicationHighDpiMode).
        ApplicationConfiguration.Initialize();

        // Squelette : pour l'instant, seule la barre. Les panneaux, le tableau de bord, etc.
        // viendront comme d'autres GlassWindow (DA §9).
        Application.Run(new BarWindow());
    }
}

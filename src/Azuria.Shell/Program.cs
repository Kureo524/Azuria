using System.Runtime.InteropServices;

namespace Azuria.Shell;

internal static class Program
{
    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(nint value);
    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
    private static readonly nint PerMonitorV2 = -4;

    // Une seule instance du shell par session ; « Azuria.exe --quitter » lui demande de quitter
    // proprement (barre des tâches et place du cadre rendues à Windows).
    private const string InstanceName = @"Local\Azuria.Shell";
    private const string QuitEventName = @"Local\Azuria.Shell.Quitter";

    [STAThread]
    private static void Main(string[] args)
    {
        using var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        if (args.Contains("--quitter"))
        {
            quit.Set();
            return;
        }

        using var instance = new Mutex(true, InstanceName, out bool first);
        if (!first) return; // déjà lancé

        // Forcer PerMonitorV2 AVANT toute fenêtre : sans ça, le process reste en PerMonitor v1 et
        // le DPI vu de l'intérieur reste faux (fenêtre mal dimensionnée sur un écran mis à l'échelle).
        SetProcessDpiAwarenessContext(PerMonitorV2);

        ApplicationConfiguration.Initialize();

        // Le shell : cadre, barre gauche, tableau de bord, panneau droit, lanceur (DA §9).
        var shell = new ShellContext();
        var anchor = new Control(); // pour revenir sur le thread d'interface
        anchor.CreateControl();
        var wait = ThreadPool.RegisterWaitForSingleObject(quit,
            (_, _) => anchor.BeginInvoke(shell.Shutdown), null, Timeout.Infinite, executeOnlyOnce: true);
        Application.Run(shell);
        wait.Unregister(null);
    }
}

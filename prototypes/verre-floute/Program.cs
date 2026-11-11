using System.Diagnostics;
using static VerreFloute.Interop;

namespace VerreFloute;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        string scenario = args.Length > 0 ? args[0].ToLowerInvariant() : "aide";
        switch (scenario)
        {
            case "fond":
                BackgroundWindow.CreateAndShow();
                BackgroundWindow.RunMessageLoop();
                return 0;

            // Q de base : la recette Manhwadle en natif — cadre conservé, Acrylic DWM.
            case "acrylic":
                Run(new GlassOptions
                {
                    Title = "acrylic (cadre + DWM)",
                    Backdrop = BackdropKind.DwmAcrylic,
                    Topmost = true, // pour rester visible au-dessus du fond de test
                });
                return 0;

            // Q1, piste 1 : découpe par région de fenêtre, sur une fenêtre sans cadre.
            case "region":
                Run(new GlassOptions
                {
                    Title = "region (popup + DWM + SetWindowRgn)",
                    Frameless = true,
                    Topmost = true,
                    Backdrop = BackdropKind.DwmAcrylic,
                    RegionClip = true,
                    ContentClip = true,
                    Radius = 18,
                });
                return 0;

            // Q1, piste 2 : flou composé par nous (pinceau d'arrière-plan de l'hôte),
            // découpe arrondie antialiasée par le compositeur.
            case "compo":
                Run(new GlassOptions
                {
                    Title = "compo (popup + HostBackdropBrush)",
                    Frameless = true,
                    Topmost = true,
                    Backdrop = BackdropKind.HostBackdrop,
                    ContentClip = true,
                    Radius = 18,
                    // touches A (accent hôte, non documenté) et D (DWMSBT) pour trouver
                    // ce qui alimente réellement le pinceau.
                });
                return 0;

            // Q2 : fenêtre sans cadre, toujours au-dessus, hors barre des tâches + Acrylic DWM.
            case "sans-cadre":
                Run(new GlassOptions
                {
                    Title = "sans-cadre (popup topmost toolwindow + DWM)",
                    Frameless = true,
                    Topmost = true,
                    ToolWindow = true,
                    Backdrop = BackdropKind.DwmAcrylic,
                    CornerPreference = DWMWCP_ROUND,
                });
                return 0;

            // Q3 : panneau 380 de large au bord droit, glissé composition (Espace)
            // et glissé fenêtre mesuré (W, X). --moteur compo|dwm
            case "glisse":
            {
                bool compo = args.Contains("--moteur") && args[Array.IndexOf(args, "--moteur") + 1] == "compo"
                             || args.Contains("compo");
                SystemParametersInfoW(SPI_GETWORKAREA, 0, out RECT work, 0);
                int wLog = 380, hLog = 720;
                Run(new GlassOptions
                {
                    Title = $"glisse ({(compo ? "HostBackdrop" : "DWM")})",
                    Frameless = true,
                    Topmost = true,
                    ToolWindow = true,
                    Backdrop = compo ? BackdropKind.HostBackdrop : BackdropKind.DwmAcrylic,
                    ContentClip = compo,
                    RegionClip = !compo,
                    Radius = 18,
                    Panel = true,
                    Width = wLog,
                    Height = hLog,
                    X = work.Right - wLog - 12,
                    Y = work.Top + 64,
                });
                return 0;
            }

            // Q4 : même fenêtre que « region », mais on suit l'état de la transparence.
            // Couper/réactiver la transparence pendant qu'elle tourne.
            case "repli":
                Run(new GlassOptions
                {
                    Title = "repli (suit la transparence de Windows)",
                    Frameless = true,
                    Topmost = true,
                    Backdrop = BackdropKind.DwmAcrylic,
                    RegionClip = true,
                    ContentClip = true,
                    Radius = 18,
                });
                return 0;

            // Contradiction à trancher : DWMWA_BORDER_COLOR sur la fenêtre d'une autre application.
            case "bordure":
                return RunBorderTest();

            default:
                Console.WriteLine("""
                    Prototype « verre flouté » d'Azuria — scénarios :

                      fond        fenêtre d'arrière-plan contrastée (à lancer d'abord, dans un autre terminal)
                      acrylic     recette Manhwadle en natif : cadre conservé + Acrylic DWM
                      region      Q1 piste 1 : popup + Acrylic DWM + découpe par région (rayon libre ?)
                      compo       Q1 piste 2 : popup + HostBackdropBrush + découpe arrondie composée
                      sans-cadre  Q2 : popup, toujours au-dessus, hors barre des tâches, Acrylic DWM
                      glisse      Q3 : panneau 380 px bord droit ; Espace = glissé composition,
                                  W = glissé fenêtre mesuré (0,20 s), X = balayage 300 px / 1 s
                                  option : « compo » pour le moteur HostBackdrop
                      repli       Q4 : suit l'activation/coupure de la transparence de Windows
                      bordure     essai DWMWA_BORDER_COLOR sur une fenêtre d'une autre application

                    Touches dans une fenêtre de verre :
                      1..5 rayon 8/14/18/22/30 · R région · C découpe composée · S repli forcé
                      A accent hôte (non documenté) · D DWMSBT on/off · L essai couche alpha · Échap quitter
                    """);
                return 0;
        }
    }

    private static void Run(GlassOptions options)
    {
        CompositionHost.EnsureInitialized();
        var window = new GlassWindow(options);
        window.Create();
        GlassWindow.RunMessageLoop();
    }

    /// <summary>Essaie de colorer la bordure de la fenêtre active d'une autre application
    /// (DWMWA_BORDER_COLOR sur un HWND qui ne nous appartient pas). Aucune injection :
    /// un simple appel DWM depuis notre processus, qui réussit ou échoue.</summary>
    private static int RunBorderTest()
    {
        uint accent = 0x00CCA48F; // #8fa4cc en COLORREF (BGR)

        Console.WriteLine("Lancement du Bloc-notes…");
        var process = Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true });
        nint foreignHwnd = 0;
        for (int i = 0; i < 50 && foreignHwnd == 0; i++)
        {
            Thread.Sleep(100);
            process?.Refresh();
            if (process is { HasExited: false } && process.MainWindowHandle != 0)
                foreignHwnd = process.MainWindowHandle;
            if (foreignHwnd == 0)
                foreignHwnd = FindWindowW("Notepad", null);
        }

        if (foreignHwnd == 0)
        {
            Console.WriteLine("Impossible de trouver la fenêtre du Bloc-notes : essai non concluant.");
            return 1;
        }

        Console.WriteLine($"HWND du Bloc-notes : 0x{foreignHwnd:X}");
        int hr = DwmSetWindowAttribute(foreignHwnd, DWMWA_BORDER_COLOR, ref accent, 4);
        Console.WriteLine($"DwmSetWindowAttribute(DWMWA_BORDER_COLOR=#8fa4cc) sur la fenêtre d'un AUTRE processus : hr=0x{hr:X8} {(hr == 0 ? "(OK — à confirmer à l'écran)" : "(ÉCHEC)")}");

        Console.WriteLine("La bordure devrait être visible sur la fenêtre du Bloc-notes au premier plan.");
        Console.WriteLine("Appuyer sur Entrée pour remettre la bordure par défaut et quitter…");
        Console.ReadLine();

        uint def = DWMWA_COLOR_DEFAULT;
        DwmSetWindowAttribute(foreignHwnd, DWMWA_BORDER_COLOR, ref def, 4);
        return 0;
    }
}

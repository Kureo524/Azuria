using System.Text.Json;
using Azuria.Shell.Native;
using Microsoft.Win32;

namespace Azuria.Shell;

/// <summary>
/// Le contexte de l'application : tient toutes les fenêtres du shell et garde la boucle de
/// messages vivante tant qu'elles existent. Organisation (inspirée de Caelestia, réécrite) : un cadre
/// permanent autour de chaque écran, d'où sortent la barre verticale à gauche, le tableau de bord en
/// haut, le lanceur en bas et le panneau à droite. Le verre de tout ça est dessiné par la surface de
/// chaque écran (<see cref="ScreenSurface"/>) ; les tiroirs n'y posent que leur page.
/// </summary>
internal sealed class ShellContext : ApplicationContext
{
    private readonly List<GlassWindow> _drawers = new();
    private readonly List<ScreenSurface> _surfaces = new();
    private readonly Taskbar? _taskbar;
    private readonly OpenWindows? _apps;
    private readonly System.Windows.Forms.Timer _fullScreenWatch = new() { Interval = 250 };

    public ShellContext()
    {
        // La barre des tâches de Windows d'abord (la place qu'elle libère revient au cadre), puis le
        // cadre : il réserve sa place, les tiroirs se calent ensuite sur la zone restante.
        _taskbar = new Taskbar();
        BuildSurfaces();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        // Appli plein écran (un jeu) : le verre de son écran se masque, les tiroirs sont suspendus.
        _fullScreenWatch.Tick += (_, _) =>
        {
            string? occupied = FullScreen.OccupiedScreen();
            foreach (var s in _surfaces) s.SetFullScreen(s.Screen.DeviceName == occupied);
        };
        _fullScreenWatch.Start();

        // À la sortie, même sur une erreur : on rend la place réservée et la barre des tâches.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreWindows();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => RestoreWindows();

        var bar = new SidePanelWindow("left.html", right: false, widthLogical: 56);       // barre principale
        var dashboard = new DashboardWindow();                                            // haut
        var sidebar = new SidePanelWindow("right.html", right: true, widthLogical: 200);  // réglages + notifs
        var launcher = new LauncherWindow();                                              // bas

        // La barre gauche remplace la barre des tâches de Windows : elle liste les applis ouvertes.
        _apps = new OpenWindows();
        _apps.Changed += () => { if (bar.Visible) SendApps(bar); };
        bar.PresenceChanged += (_, present) => { if (present) SendApps(bar); };

        bar.MessageReceived += msg =>
        {
            if (msg == "launcher") launcher.Toggle(Cursor.Position);   // le logo ouvre / range le lanceur
            else if (msg == "quit") Shutdown();                        // bouton d'alimentation (provisoire)
            else if (msg == "apps") SendApps(bar);                     // la page est prête
            else if (msg.StartsWith("app:") && long.TryParse(msg[4..], out long h))
            {
                _apps.Activate((nint)h);
                SendApps(bar);
            }
        };

        foreach (var w in new GlassWindow[] { bar, dashboard, sidebar, launcher })
        {
            w.Moved += OnDrawerMoved;
            w.PresenceChanged += OnDrawerPresence;
            w.FormClosed += (_, _) => Shutdown(); // fermer une fenêtre du shell ferme tout Azuria
            _drawers.Add(w);
        }
        foreach (var w in _drawers) w.Show();

        // Fin de session Windows : on rend la barre des tâches avant de partir.
        SystemEvents.SessionEnding += (_, _) => Shutdown();
    }

    private bool _down;

    /// <summary>
    /// Quitte Azuria proprement, quelle que soit la demande (fenêtre fermée, <c>taskkill</c>, fin de
    /// session) : barre des tâches et place réservée rendues à Windows, puis fin de la boucle.
    /// </summary>
    public void Shutdown()
    {
        if (_down) return;
        _down = true;
        _fullScreenWatch.Stop();
        _apps?.Dispose();
        _taskbar?.Dispose();
        DisposeSurfaces();
        foreach (var d in _drawers.ToList()) d.Dispose();
        _drawers.Clear();
        ExitThread();
    }

    private void RestoreWindows()
    {
        foreach (var s in _surfaces) s.ReleaseReservation();
        _taskbar?.Restore();
    }

    // La liste des applis ouvertes, envoyée à la barre (icônes en PNG, la page dessine le reste).
    private void SendApps(GlassWindow bar)
    {
        if (_apps is null) return;
        var items = _apps.List().Select(w => new { id = (long)w.Handle, title = w.Title, icon = w.Icon, active = w.Active });
        bar.PostJson(JsonSerializer.Serialize(new { type = "apps", items }));
    }

    // Le verre d'un tiroir est dessiné par la surface de son écran, et seulement elle.
    private void OnDrawerMoved(GlassWindow drawer)
    {
        var screen = drawer.AnchorScreen.DeviceName;
        foreach (var s in _surfaces)
            s.Track(drawer, drawer.Visible && s.Screen.DeviceName == screen);
    }

    // Un tiroir latéral occupe son côté : ses congés remplacent les coins du cadre de ce côté.
    private void OnDrawerPresence(GlassWindow drawer, bool present)
    {
        if (drawer.Edge is not (DrawerEdge.Left or DrawerEdge.Right)) return;
        SurfaceOf(drawer.AnchorScreen)?.SetSideOccupied(drawer.Edge, present);
    }

    private ScreenSurface? SurfaceOf(Screen s) => _surfaces.FirstOrDefault(f => f.Screen.DeviceName == s.DeviceName);

    private void BuildSurfaces()
    {
        foreach (var screen in Screen.AllScreens)
        {
            var s = new ScreenSurface(screen);
            s.FullScreenChanged += (_, _) => UpdateSuspension();
            s.Relaid += () => { foreach (var d in _drawers) OnDrawerMoved(d); }; // le verre des tiroirs se recale
            s.CloseRequested += Shutdown;
            s.Start();
            _surfaces.Add(s);
        }
    }

    // Une appli en plein écran (un jeu) sur n'importe quel écran : aucun tiroir ne s'ouvre.
    private void UpdateSuspension() =>
        GlassWindow.Suspended = _surfaces.Any(s => s.IsFullScreen);

    // Écrans branchés, débranchés ou redimensionnés : on reconstruit le verre. Windows envoie aussi
    // cet évènement pour des changements sans effet sur les écrans (barre des tâches masquée…) :
    // on l'ignore alors, sinon la surface repasserait au-dessus des tiroirs.
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        string now = ScreensSignature();
        if (now == _screens) return;
        _screens = now;
        DisposeSurfaces();
        BuildSurfaces();
        foreach (var d in _drawers)
        {
            d.RaiseAbove();     // les pages restent au-dessus du verre
            OnDrawerMoved(d);
        }
    }

    private string _screens = ScreensSignature();

    private static string ScreensSignature() =>
        string.Join(";", Screen.AllScreens.Select(s => $"{s.DeviceName}:{s.Bounds}"));

    private void DisposeSurfaces()
    {
        foreach (var s in _surfaces) s.Dispose();
        _surfaces.Clear();
    }
}

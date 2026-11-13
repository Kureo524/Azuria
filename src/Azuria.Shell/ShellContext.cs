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

    public ShellContext()
    {
        // Le cadre d'abord : il réserve sa place, les tiroirs se calent ensuite sur la zone restante.
        BuildSurfaces();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            foreach (var s in _surfaces) s.ReleaseReservation(); // rend la place réservée
        };

        var bar = new SidePanelWindow("left.html", right: false, widthLogical: 56);       // barre principale
        var dashboard = new DashboardWindow();                                            // haut
        var sidebar = new SidePanelWindow("right.html", right: true, widthLogical: 200);  // réglages + notifs
        var launcher = new LauncherWindow();                                              // bas

        // Le logo Azuria de la barre ouvre / range le lanceur, sur l'écran du curseur.
        bar.MessageReceived += msg =>
        {
            if (msg == "launcher") launcher.Toggle(Cursor.Position);
        };

        foreach (var w in new GlassWindow[] { bar, dashboard, sidebar, launcher })
        {
            w.Moved += OnDrawerMoved;
            w.PresenceChanged += OnDrawerPresence;
            w.FormClosed += (_, _) =>
            {
                _drawers.Remove(w);
                if (_drawers.Count == 0)
                {
                    DisposeSurfaces();
                    ExitThread();
                }
            };
            _drawers.Add(w);
        }
        foreach (var w in _drawers) w.Show();
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
            s.Start();
            _surfaces.Add(s);
        }
    }

    // Une appli en plein écran (un jeu) sur n'importe quel écran : aucun tiroir ne s'ouvre.
    private void UpdateSuspension() =>
        GlassWindow.Suspended = _surfaces.Any(s => s.IsFullScreen);

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        DisposeSurfaces();
        BuildSurfaces();
        foreach (var d in _drawers) OnDrawerMoved(d);
    }

    private void DisposeSurfaces()
    {
        foreach (var s in _surfaces) s.Dispose();
        _surfaces.Clear();
    }
}

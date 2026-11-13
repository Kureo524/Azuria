using Microsoft.Win32;

namespace Azuria.Shell;

/// <summary>
/// Le contexte de l'application : tient toutes les fenêtres du shell et garde la boucle de
/// messages vivante tant qu'elles existent. Organisation (inspirée de Caelestia, réécrite) : un cadre
/// permanent autour de chaque écran, d'où sortent la barre verticale à gauche, le tableau de bord en
/// haut, le lanceur en bas et le panneau à droite.
/// </summary>
internal sealed class ShellContext : ApplicationContext
{
    private readonly List<Form> _windows = new();
    private readonly List<ScreenFrame> _frames = new();

    public ShellContext()
    {
        // Le cadre d'abord : il réserve sa place, les tiroirs se calent ensuite sur la zone restante.
        BuildFrames();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            foreach (var f in _frames) f.ReleaseReservation(); // rend la place réservée
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
            w.PresenceChanged += OnDrawerPresence;
            Add(w);
        }
        foreach (var w in _windows) w.Show();
        foreach (var f in _frames) f.BringToTop();
    }

    // Un tiroir sort : le cadre de son écran repasse au-dessus (le tiroir sort de dessous), et un
    // tiroir latéral libère les coins du cadre de son côté (ses propres congés les remplacent).
    private void OnDrawerPresence(GlassWindow drawer, bool present)
    {
        var frame = FrameOf(drawer.AnchorScreen);
        if (frame is null) return;
        if (drawer.Edge is DrawerEdge.Left or DrawerEdge.Right) frame.SetSideOccupied(drawer.Edge, present);
        if (present) frame.BringToTop();
    }

    private ScreenFrame? FrameOf(Screen s) => _frames.FirstOrDefault(f => f.Screen.DeviceName == s.DeviceName);

    private void BuildFrames()
    {
        foreach (var s in Screen.AllScreens)
        {
            var f = new ScreenFrame(s);
            f.FullScreenChanged += (_, _) => UpdateSuspension();
            f.Start();
            _frames.Add(f);
        }
    }

    // Une appli en plein écran (un jeu) sur n'importe quel écran : aucun tiroir ne s'ouvre.
    private void UpdateSuspension() =>
        GlassWindow.Suspended = _frames.Any(f => f.IsFullScreen);

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        DisposeFrames();
        BuildFrames();
        foreach (var f in _frames) f.BringToTop();
    }

    private void DisposeFrames()
    {
        foreach (var f in _frames) f.Dispose();
        _frames.Clear();
    }

    private void Add(Form w)
    {
        w.FormClosed += (_, _) =>
        {
            _windows.Remove(w);
            if (_windows.Count == 0)
            {
                DisposeFrames();
                ExitThread();
            }
        };
        _windows.Add(w);
    }
}

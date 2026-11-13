using System.Runtime.InteropServices;
using Azuria.Shell.Native;

namespace Azuria.Shell;

/// <summary>
/// Le verre d'un écran (organisation inspirée de Caelestia, réécrite ici) : une seule fenêtre,
/// transparente aux clics, qui dessine le cadre permanent autour de la zone de travail (fine bande,
/// coins intérieurs arrondis) ET le verre des tiroirs qui en sortent. Tout partage le même flou,
/// d'un seul tenant : pas de différence de teinte entre le cadre et un panneau. Les fenêtres des
/// tiroirs ne portent que leur page (WebView2), posée au-dessus.
///
/// La place du cadre est réservée (une barre d'application de Windows par bord) : les fenêtres
/// agrandies s'arrêtent à son bord intérieur. Quand une appli passe en plein écran (un jeu), le verre
/// se masque et les tiroirs sont suspendus : Azuria ne dessine jamais par-dessus un jeu (CLAUDE.md).
/// </summary>
internal sealed class ScreenSurface : IDisposable
{
    private static readonly DrawerEdge[] Edges = { DrawerEdge.Top, DrawerEdge.Bottom, DrawerEdge.Left, DrawerEdge.Right };

    private readonly SurfaceWindow _window;
    private readonly Dictionary<DrawerEdge, AppBarHost> _bars = new();
    private readonly Dictionary<GlassWindow, (GlassLayer Layer, Size Size)> _drawers = new();
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 300 };
    private readonly System.Windows.Forms.Timer _relayout = new() { Interval = 100 };
    private GlassSurface? _glass;
    private GlassLayer? _frame;
    private Rectangle _outer;  // le cadre, bord extérieur (écran, hors barre des tâches)
    private Rectangle _inner;  // zone de travail = bord intérieur du cadre
    private bool _hideLeftCorners, _hideRightCorners, _fullScreen, _busy;

    public Screen Screen { get; }
    public bool IsFullScreen => _fullScreen;

    /// <summary>Une appli passe en plein écran sur cet écran (true) ou le quitte (false).</summary>
    public event Action<ScreenSurface, bool>? FullScreenChanged;

    /// <summary>Windows demande la fermeture (taskkill, fin de session) : Azuria doit quitter proprement.</summary>
    public event Action? CloseRequested;

    internal void OnCloseRequested() => CloseRequested?.Invoke();

    /// <summary>Le bord intérieur du cadre a bougé (barre des tâches déplacée, DPI…).</summary>
    public event Action? Relaid;

    public ScreenSurface(Screen screen)
    {
        Screen = screen;
        _window = new SurfaceWindow(this);
        foreach (var e in Edges) _bars[e] = new AppBarHost(this);
        _debounce.Tick += (_, _) => { _debounce.Stop(); Reserve(); Layout(); };
        // Windows applique la nouvelle zone de travail un peu après nos réservations : on se recale.
        _relayout.Tick += (_, _) => { _relayout.Stop(); Layout(); };
    }

    public void Start()
    {
        foreach (var b in _bars.Values) AppBar.Register(b.Handle);
        Reserve();
        _window.CreateControl();
        _glass = new GlassSurface(_window.Handle) { Blur = GlassWindow.TransparencyEnabled() };
        _frame = _glass.AddLayer(new Rectangle(Point.Empty, Screen.Bounds.Size));
        Layout();
        _window.Show();
    }

    /// <summary>Réserve les quatre bandes auprès de Windows (la zone de travail rétrécit d'autant).</summary>
    private void Reserve()
    {
        _busy = true; // nos propres réservations déclenchent des « position changée » : on les ignore
        int t = Monitors.Scale(Screen, ShellMetrics.Frame);
        foreach (var e in Edges) AppBar.Reserve(_bars[e].Handle, e, Screen.Bounds, t);
        _busy = false;
    }

    /// <summary>Place la fenêtre sur le cadre et redessine celui-ci autour de la zone de travail.</summary>
    private void Layout()
    {
        if (_glass is null || _frame is null) return;
        _inner = Monitors.WorkArea(Screen);
        int t = Monitors.Scale(Screen, ShellMetrics.Frame);
        _outer = Rectangle.Inflate(_inner, t, t);
        _window.Bounds = _outer;

        _frame.SetArea(new Rectangle(Point.Empty, _outer.Size));
        _frame.SetShape(FrameShape());
        _frame.Place(Point.Empty, true);

        // Les tiroirs sont rognés au bord intérieur du cadre (ils en sortent).
        var inner = Local(_inner);
        foreach (var (drawer, d) in _drawers) d.Layer.SetArea(inner);
        Relaid?.Invoke();
    }

    // Le cadre : quatre bandes d'épaisseur t, et les coins intérieurs arrondis (congés).
    private GlassShape FrameShape()
    {
        int t = Monitors.Scale(Screen, ShellMetrics.Frame);
        int r = Monitors.Scale(Screen, ShellMetrics.Fillet);
        int w = _outer.Width, h = _outer.Height;
        var s = new GlassShape()
            .Rectangle(0, 0, w, t)
            .Rectangle(0, h - t, w, t)
            .Rectangle(0, t, t, h - 2 * t)
            .Rectangle(w - t, t, t, h - 2 * t);
        if (!_hideLeftCorners)
        {
            s.Concave(t, t, r, t + r, t + r);
            s.Concave(t, h - t - r, r, t + r, h - t - r);
        }
        if (!_hideRightCorners)
        {
            s.Concave(w - t - r, t, r, w - t - r, t + r);
            s.Concave(w - t - r, h - t - r, r, w - t - r, h - t - r);
        }
        return s;
    }

    private Rectangle Local(Rectangle screenRect) =>
        new(screenRect.X - _outer.X, screenRect.Y - _outer.Y, screenRect.Width, screenRect.Height);

    /// <summary>
    /// Dessine (ou masque) le verre d'un tiroir, à sa position actuelle. Appelé à chaque pas de son
    /// glissé : seul un décalage change, le contour n'est recalculé que si la taille change.
    /// </summary>
    public void Track(GlassWindow drawer, bool visible)
    {
        if (_glass is null) return;
        if (!_drawers.TryGetValue(drawer, out var d))
        {
            d = (_glass.AddLayer(Local(_inner)), Size.Empty);
            _drawers[drawer] = d;
        }
        if (visible && d.Size != drawer.Size)
        {
            d.Layer.SetShape(drawer.Shape);
            _drawers[drawer] = d = (d.Layer, drawer.Size);
        }
        var at = drawer.Location;
        d.Layer.Place(new Point(at.X - _outer.X, at.Y - _outer.Y), visible && !_fullScreen);
    }

    /// <summary>
    /// Un tiroir latéral occupe (ou libère) le côté <paramref name="side"/> : ses propres congés
    /// remplacent alors les coins intérieurs du cadre de ce côté.
    /// </summary>
    public void SetSideOccupied(DrawerEdge side, bool occupied)
    {
        if (side == DrawerEdge.Left) _hideLeftCorners = occupied;
        else if (side == DrawerEdge.Right) _hideRightCorners = occupied;
        else return;
        _frame?.SetShape(FrameShape());
    }

    internal void OnAppBarNotify(int code, nint lParam)
    {
        switch (code)
        {
            case AppBar.PosChanged:
                if (!_busy) { _debounce.Stop(); _debounce.Start(); }
                break;
            // AppBar.FullScreenApp est ignoré : sans barre des tâches, notre propre surface couvre
            // l'écran et Windows la prend pour une appli plein écran. On détecte nous-mêmes (FullScreen).
        }
    }

    /// <summary>
    /// Une appli plein écran (un jeu) occupe cet écran : le verre se masque. Appelé par le
    /// <see cref="ShellContext"/>, qui surveille la fenêtre au premier plan.
    /// </summary>
    public void SetFullScreen(bool fs)
    {
        if (fs == _fullScreen) return;
        _fullScreen = fs;
        _window.Visible = !fs;
        FullScreenChanged?.Invoke(this, fs);
    }

    internal void OnWorkAreaChanged() { _relayout.Stop(); _relayout.Start(); }

    internal void OnSettingChange()
    {
        if (_glass is not null) _glass.Blur = GlassWindow.TransparencyEnabled();
    }

    internal void OnDpiChanged() { _debounce.Stop(); _debounce.Start(); }

    /// <summary>Rend la place réservée à Windows (sûr depuis n'importe quel thread).</summary>
    public void ReleaseReservation()
    {
        foreach (var b in _bars.Values)
            if (b.Handle != 0) AppBar.Unregister(b.Handle);
    }

    public void Dispose()
    {
        _debounce.Dispose();
        _relayout.Dispose();
        ReleaseReservation();
        foreach (var b in _bars.Values) b.DestroyHandle();
        _bars.Clear();
        _window.Dispose();
    }
}

/// <summary>
/// La fenêtre qui porte le verre d'un écran : sans contenu propre, toujours au-dessus (sous les
/// tiroirs), jamais active, et transparente aux clics (couche « layered » + transparent).
/// </summary>
internal sealed class SurfaceWindow : Form
{
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WM_CLOSE = 0x0010;
    private const int WM_DPICHANGED = 0x02E0;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    private const uint LWA_ALPHA = 0x2;

    private readonly ScreenSurface _owner;

    public SurfaceWindow(ScreenSurface owner)
    {
        _owner = owner;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP
                        | WS_EX_LAYERED | WS_EX_TRANSPARENT; // les clics traversent
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;
    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetLayeredWindowAttributes(Handle, 0, 255, LWA_ALPHA); // couche pleinement opaque
        Dwm.SetCorner(Handle, Dwm.Corner.DoNotRound);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_DPICHANGED:
                _owner.OnDpiChanged();
                m.Result = 0;
                return;
            case WM_SETTINGCHANGE:
                if (m.WParam == 0x2F /* SPI_SETWORKAREA */) _owner.OnWorkAreaChanged();
                _owner.OnSettingChange();
                break;
            case WM_CLOSE:
                // Demande de fermeture venue de Windows (taskkill…) : tout Azuria quitte proprement.
                _owner.OnCloseRequested();
                return;
        }
        base.WndProc(ref m);
    }
}

/// <summary>Fenêtre invisible qui représente une bande du cadre auprès de Windows (barre d'application).</summary>
internal sealed class AppBarHost : NativeWindow
{
    private readonly ScreenSurface _owner;

    public AppBarHost(ScreenSurface owner)
    {
        _owner = owner;
        CreateHandle(new CreateParams { ExStyle = 0x80 /* WS_EX_TOOLWINDOW */ });
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == (int)AppBar.CallbackMessage)
        {
            _owner.OnAppBarNotify((int)m.WParam, m.LParam);
            return;
        }
        base.WndProc(ref m);
    }
}

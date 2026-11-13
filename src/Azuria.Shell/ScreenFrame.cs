using System.Runtime.InteropServices;
using Azuria.Shell.Native;

namespace Azuria.Shell;

/// <summary>
/// Le cadre permanent d'un écran (organisation inspirée de Caelestia, réécrite ici) : une fine bande
/// de verre le long des quatre bords de la zone de travail, aux coins intérieurs arrondis (congés
/// concaves). Les tiroirs en sortent. Chaque bande est une barre d'application de Windows : la place
/// est réservée, les fenêtres agrandies s'arrêtent au bord intérieur du cadre.
///
/// Quand une appli passe en plein écran (un jeu), le cadre se masque et les tiroirs sont suspendus :
/// Azuria ne dessine jamais par-dessus un jeu (CLAUDE.md).
/// </summary>
internal sealed class ScreenFrame : IDisposable
{
    private static readonly DrawerEdge[] Edges = { DrawerEdge.Top, DrawerEdge.Bottom, DrawerEdge.Left, DrawerEdge.Right };

    private readonly Dictionary<DrawerEdge, FrameStrip> _strips = new();
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 300 };
    private bool _hideLeftCorners, _hideRightCorners, _fullScreen, _busy;

    public Screen Screen { get; }

    public bool IsFullScreen => _fullScreen;

    /// <summary>Une appli passe en plein écran sur cet écran (true) ou le quitte (false).</summary>
    public event Action<ScreenFrame, bool>? FullScreenChanged;

    public ScreenFrame(Screen screen)
    {
        Screen = screen;
        foreach (var e in Edges) _strips[e] = new FrameStrip(this);
        _debounce.Tick += (_, _) => { _debounce.Stop(); Reserve(); Layout(); };
    }

    public void Start()
    {
        foreach (var (edge, s) in _strips)
        {
            s.CreateControl();
            AppBar.Register(s.Handle);
        }
        Reserve();
        Layout();
        foreach (var s in _strips.Values) s.Show();
    }

    /// <summary>Réserve les quatre bandes auprès de Windows (la zone de travail rétrécit d'autant).</summary>
    private void Reserve()
    {
        _busy = true; // nos propres réservations déclenchent des « position changée » : on les ignore
        int t = Monitors.Scale(Screen, ShellMetrics.Frame);
        foreach (var e in Edges) AppBar.Reserve(_strips[e].Handle, e, Screen.Bounds, t);
        _busy = false;
    }

    /// <summary>Place les bandes autour de la zone de travail (qui exclut déjà le cadre réservé).</summary>
    public void Layout()
    {
        var inner = Monitors.WorkArea(Screen);
        int t = Monitors.Scale(Screen, ShellMetrics.Frame);
        int r = Monitors.Scale(Screen, ShellMetrics.Fillet);
        int w = inner.Width + 2 * t;
        int x = inner.Left - t;

        // Bande haute : la bande, et sous elle les deux coins intérieurs arrondis.
        var top = new GlassShape().Rectangle(0, 0, w, t);
        if (!_hideLeftCorners) top.Concave(t, t, r, t + r, t + r);
        if (!_hideRightCorners) top.Concave(w - t - r, t, r, w - t - r, t + r);
        _strips[DrawerEdge.Top].Place(new Rectangle(x, inner.Top - t, w, t + r), top);

        // Bande basse : au-dessus d'elle les deux coins intérieurs.
        var bottom = new GlassShape().Rectangle(0, r, w, t);
        if (!_hideLeftCorners) bottom.Concave(t, 0, r, t + r, 0);
        if (!_hideRightCorners) bottom.Concave(w - t - r, 0, r, w - t - r, 0);
        _strips[DrawerEdge.Bottom].Place(new Rectangle(x, inner.Bottom - r, w, t + r), bottom);

        // Bandes latérales, entre les deux.
        _strips[DrawerEdge.Left].Place(new Rectangle(x, inner.Top, t, inner.Height),
            new GlassShape().Rectangle(0, 0, t, inner.Height));
        _strips[DrawerEdge.Right].Place(new Rectangle(inner.Right, inner.Top, t, inner.Height),
            new GlassShape().Rectangle(0, 0, t, inner.Height));
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
        Layout();
    }

    /// <summary>Repasse le cadre au-dessus des tiroirs (qui semblent ainsi sortir de dessous).</summary>
    public void BringToTop()
    {
        foreach (var s in _strips.Values)
            if (s.IsHandleCreated) SetWindowPos(s.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    internal void OnAppBarNotify(int code, nint lParam)
    {
        switch (code)
        {
            case AppBar.PosChanged:
                if (!_busy) { _debounce.Stop(); _debounce.Start(); }
                break;
            case AppBar.FullScreenApp:
                bool fs = lParam != 0;
                if (fs == _fullScreen) return;
                _fullScreen = fs;
                foreach (var s in _strips.Values) s.Visible = !fs;
                if (!fs) BringToTop();
                FullScreenChanged?.Invoke(this, fs);
                break;
        }
    }

    internal void OnDpiChanged() { _debounce.Stop(); _debounce.Start(); }

    /// <summary>Rend la place réservée à Windows (sûr depuis n'importe quel thread).</summary>
    public void ReleaseReservation()
    {
        foreach (var s in _strips.Values)
            if (s.IsHandleCreated) AppBar.Unregister(s.Handle);
    }

    public void Dispose()
    {
        _debounce.Dispose();
        foreach (var s in _strips.Values)
        {
            if (s.IsHandleCreated) AppBar.Unregister(s.Handle);
            s.Dispose();
        }
        _strips.Clear();
    }

    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    private static readonly nint HWND_TOPMOST = -1;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
}

/// <summary>Une bande du cadre : fenêtre de verre sans contenu, qui ne prend jamais le focus.</summary>
internal sealed class FrameStrip : Form
{
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WM_DPICHANGED = 0x02E0;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    private readonly ScreenFrame _owner;
    private GlassSurface? _glass;
    private GlassShape? _shape;

    public FrameStrip(ScreenFrame owner)
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
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;
    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Dwm.SetCorner(Handle, Dwm.Corner.DoNotRound);
        _glass = new GlassSurface(Handle) { Blur = GlassWindow.TransparencyEnabled() };
        if (_shape is not null) _glass.SetShape(_shape, Width, Height);
    }

    public void Place(Rectangle bounds, GlassShape shape)
    {
        _shape = shape;
        Bounds = bounds;
        _glass?.SetShape(shape, bounds.Width, bounds.Height);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == (int)AppBar.CallbackMessage)
        {
            _owner.OnAppBarNotify((int)m.WParam, m.LParam);
            return;
        }
        switch (m.Msg)
        {
            case WM_DPICHANGED:
                _owner.OnDpiChanged();
                m.Result = 0;
                return;
            case WM_SETTINGCHANGE:
                if (_glass is not null) _glass.Blur = GlassWindow.TransparencyEnabled();
                break;
        }
        base.WndProc(ref m);
    }
}

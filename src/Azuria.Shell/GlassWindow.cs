using System.Diagnostics;
using System.Runtime.InteropServices;
using Azuria.Shell.Native;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace Azuria.Shell;

/// <summary>Bord d'écran auquel un tiroir est collé (None = tiroir flottant).</summary>
internal enum DrawerEdge { None, Top, Bottom, Left, Right }

/// <summary>Mesures communes du shell (px logiques, DA §8–9).</summary>
internal static class ShellMetrics
{
    /// <summary>Épaisseur du cadre permanent autour de la zone de travail.</summary>
    public const int Frame = 8;
    /// <summary>Rayon des coins intérieurs du cadre et des congés où un tiroir le rejoint.</summary>
    public const int Fillet = 18;
}

/// <summary>
/// Fenêtre de verre d'Azuria : hôte natif sans cadre, toujours au-dessus, hors barre des tâches, qui
/// affiche une page de l'UI (HTML/CSS) dans un WebView2 transparent, posé sur le verre composé par
/// <see cref="GlassSurface"/> (flou + voile à la forme exacte du tiroir).
///
/// Les panneaux sont des « tiroirs » qui sortent du cadre de l'écran (organisation inspirée de
/// Caelestia, entièrement réécrite ici) : collés au bord intérieur du cadre, avec des congés concaves
/// là où ils le rejoignent, coins arrondis côté libre. Un tiroir se révèle en glissant depuis son
/// bord (seule la position bouge, cadencée par DWM), au survol du bord ou sur commande.
/// </summary>
internal class GlassWindow : Form
{
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WM_DPICHANGED = 0x02E0;
    private const int SPI_SETWORKAREA = 0x002F;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    private readonly string _page;
    private readonly WebView2 _web = new();
    private GlassSurface? _glass;
    private bool _blurAvailable = true;

    // Géométrie (pixels physiques), fixée par la classe dérivée.
    private Rectangle _hidden;      // position cachée (hors écran)
    private Rectangle _shown;       // position révélée
    private Rectangle _triggerZone; // curseur ici (caché) → on révèle
    private Rectangle _keepZone;    // curseur ici (révélé) → on reste ; sinon on range
    private bool _shownState;
    private bool _geometryReady;
    private bool _animating;

    // Survol (sondage du curseur) et stabilisation après le premier affichage
    private readonly System.Windows.Forms.Timer _hover = new() { Interval = 30 };
    private readonly System.Windows.Forms.Timer _settle = new() { Interval = 50 };
    private int _settleTicks;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    /// <summary>Message envoyé par la page (<c>chrome.webview.postMessage</c>), sauf « close » qui range le tiroir.</summary>
    public event Action<string>? MessageReceived;

    /// <summary>Le tiroir commence à sortir (true) ou vient de finir de rentrer (false).</summary>
    public event Action<GlassWindow, bool>? PresenceChanged;

    /// <summary>
    /// Vrai quand une appli est en plein écran (jeu…) : aucun tiroir ne s'ouvre au survol, et ceux qui
    /// sont ouverts se rangent. Azuria ne dessine jamais par-dessus un jeu (CLAUDE.md).
    /// </summary>
    public static bool Suspended { get; set; }

    protected GlassWindow(string page)
    {
        _page = page;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None; // on gère les tailles en pixels physiques

        _web.Dock = DockStyle.Fill;         // taille de fenêtre constante → pas de re-layout
        _web.DefaultBackgroundColor = Color.Transparent;
        Controls.Add(_web);

        _hover.Tick += OnHoverTick;
        _settle.Tick += (_, _) =>
        {
            LayoutGeometry();
            if (++_settleTicks >= 6) { _settle.Stop(); _settleTicks = 0; }
        };
    }

    // ----- ce que les tiroirs dérivés peuvent régler -----

    /// <summary>Écran d'ancrage (par défaut l'écran principal).</summary>
    public virtual Screen AnchorScreen => Screen.PrimaryScreen ?? Screen.AllScreens[0];

    /// <summary>Bord du cadre d'où sort le tiroir.</summary>
    public virtual DrawerEdge Edge => DrawerEdge.None;

    /// <summary>Ne vole pas le focus (vrai pour les barres/panneaux, faux pour le lanceur où l'on tape).</summary>
    protected virtual bool NoActivate => true;

    /// <summary>Révélé au survol de son bord (faux pour un tiroir ouvert sur commande).</summary>
    protected virtual bool HoverDriven => true;

    /// <summary>Appelé quand la fenêtre doit (re)calculer sa géométrie (affichage, DPI, écran).</summary>
    protected virtual void LayoutGeometry() { }

    /// <summary>Appelé juste avant une révélation au survol, avec la position du curseur.</summary>
    protected virtual void PrepareReveal(Point cursor) { }

    /// <summary>Forme du verre pour une fenêtre de <paramref name="w"/>×<paramref name="h"/> px.</summary>
    protected virtual void BuildShape(GlassShape s, int w, int h) =>
        s.Body(0, 0, w, h, Scale(ShellMetrics.Fillet), true, true, true, true);

    /// <summary>Marges (px logiques) entre la fenêtre et la zone utile de la page : les congés.</summary>
    protected virtual Padding ContentInsets => Padding.Empty;

    public bool IsShown => _shownState;

    /// <summary>Zone de travail de l'écran d'ancrage : intérieur du cadre, hors barre des tâches.</summary>
    protected Rectangle WorkArea => Monitors.WorkArea(AnchorScreen);

    /// <summary>Convertit une taille logique (px à 100 %) en pixels physiques pour l'écran d'ancrage.</summary>
    protected int Scale(int logical) => Monitors.Scale(AnchorScreen, logical);

    /// <summary>
    /// Configure la révélation par glissement. <paramref name="triggerZone"/> : où amener le curseur
    /// pour révéler ; <paramref name="keepZone"/> : où il doit rester pour que ça le reste.
    /// </summary>
    protected void ConfigureReveal(Rectangle hidden, Rectangle shown, Rectangle triggerZone, Rectangle keepZone)
    {
        _hidden = hidden;
        _shown = shown;
        _triggerZone = triggerZone;
        _keepZone = keepZone;
        _geometryReady = true;
        if (!_animating)
        {
            Bounds = _shownState ? shown : hidden;
            // Au repos caché : fenêtre réellement invisible (ne déborde sur aucun autre écran).
            Visible = _shownState;
        }
    }

    /// <summary>Révèle le tiroir (sur commande).</summary>
    public void Reveal()
    {
        if (_geometryReady && !_animating && !_shownState && !Suspended) Slide(true);
    }

    /// <summary>Range le tiroir (sur commande).</summary>
    public void Conceal()
    {
        if (_geometryReady && !_animating && _shownState) Slide(false);
    }

    // ----- fenêtre native -----

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;                 // hors Alt-Tab et hors barre des tâches
            cp.ExStyle |= WS_EX_NOREDIRECTIONBITMAP;        // seuls le verre composé et la page s'affichent
            if (NoActivate) cp.ExStyle |= WS_EX_NOACTIVATE; // ne vole pas le focus
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => NoActivate;

    // Rien à peindre : le fond est le verre composé (et la fenêtre n'a pas de surface GDI).
    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        LayoutGeometry();
        _settle.Start(); // re-calcule quelques fois le temps que tout se stabilise
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyShape(); // la forme ne dépend que de la taille : elle ne bouge pas pendant un glissé
    }

    private void ApplyShape()
    {
        if (_glass is null || Width <= 0 || Height <= 0) return;
        var s = new GlassShape();
        BuildShape(s, Width, Height);
        _glass.SetShape(s, Width, Height);
    }

    protected override async void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        Dwm.UseDarkMode(Handle);
        Dwm.SetCorner(Handle, Dwm.Corner.DoNotRound); // la forme est la nôtre
        _blurAvailable = TransparencyEnabled();
        _glass = new GlassSurface(Handle) { Blur = _blurAvailable };
        ApplyShape();

        await InitWebAsync();
    }

    private async Task InitWebAsync()
    {
        string dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Azuria", "WebView2");
        Directory.CreateDirectory(dataDir);

        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: dataDir);
        await _web.EnsureCoreWebView2Async(env);

        var core = _web.CoreWebView2;
        string webRoot = Path.Combine(AppContext.BaseDirectory, "web");
        core.SetVirtualHostNameToFolderMapping(
            "azuria.local", webRoot, CoreWebView2HostResourceAccessKind.Allow);

        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;

        core.WebMessageReceived += (_, e) =>
        {
            string msg;
            try { msg = e.TryGetWebMessageAsString(); } catch { return; }
            if (msg == "close") Conceal();
            else MessageReceived?.Invoke(msg);
        };

        ApplyBlurClass();
        // La page reçoit la zone utile (hors congés) en px CSS (drawer.js).
        var i = ContentInsets;
        string query = $"?l={i.Left}&t={i.Top}&r={i.Right}&b={i.Bottom}";
        core.Navigate($"https://azuria.local/{_page}{query}");

        if (HoverDriven) _hover.Start();
    }

    // ----- révélation par glissement -----

    private void OnHoverTick(object? sender, EventArgs e)
    {
        if (!_geometryReady || _animating) return;
        if (Suspended)
        {
            if (_shownState) Slide(false);
            return;
        }
        GetCursorPos(out POINT p);
        var cursor = new Point(p.X, p.Y);
        Rectangle hot = _shownState ? _keepZone : _triggerZone;
        bool want = hot.Contains(cursor);
        if (want != _shownState)
        {
            if (want) PrepareReveal(cursor); // ex. se recentrer sur l'écran du curseur
            Slide(want);
        }
    }

    /// <summary>
    /// Glisse la fenêtre entre caché et révélé, cadencé par DWM (synchronisé au rafraîchissement).
    /// Seule la position change (taille constante), donc rien ne se remet en page pendant le glissé.
    /// </summary>
    private void Slide(bool shown)
    {
        _shownState = shown;
        _animating = true;
        try
        {
            // Apparaît au point caché avant de glisser vers l'intérieur.
            if (shown && !Visible) { Bounds = _hidden; Visible = true; }
            if (shown) PresenceChanged?.Invoke(this, true); // le cadre se range au-dessus, libère ses coins
            var from = Bounds;
            var to = shown ? _shown : _hidden;
            double dur = shown ? 200 : 140;   // DA §11 : ouverture ~0,2 s, fermeture 0,14 s
            var sw = Stopwatch.StartNew();
            while (true)
            {
                double t = sw.Elapsed.TotalMilliseconds;
                if (t >= dur) break;
                double p = t / dur;
                double e = shown ? Bezier(0.2, 0, 0, 1, p)   // entrée : décélération
                                 : Bezier(0.3, 0, 1, 1, p);  // sortie : accélération
                SetWindowPos(Handle, 0, Lerp(from.X, to.X, e), Lerp(from.Y, to.Y, e), 0, 0,
                    SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                Dwm.Flush();
            }
            SetWindowPos(Handle, 0, to.X, to.Y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            // Une fois rangée, la fenêtre est vraiment masquée.
            if (!shown)
            {
                Visible = false;
                PresenceChanged?.Invoke(this, false);
            }
        }
        finally { _animating = false; }

        if (shown && !NoActivate)
        {
            // Tiroir où l'on tape (lanceur) : on prend le focus et on prévient la page.
            Activate();
            _web.Focus();
            _web.CoreWebView2?.PostWebMessageAsString("shown");
        }
    }

    private static int Lerp(int a, int b, double t) => (int)Math.Round(a + (b - a) * t);

    // Courbe de Bézier cubique (x->y) résolue par Newton, comme en CSS.
    private static double Bezier(double x1, double y1, double x2, double y2, double x)
    {
        x = Math.Clamp(x, 0, 1);
        double t = x;
        for (int i = 0; i < 6; i++)
        {
            double xt = Bez(x1, x2, t) - x;
            double dxt = BezD(x1, x2, t);
            if (Math.Abs(dxt) < 1e-6) break;
            t = Math.Clamp(t - xt / dxt, 0, 1);
        }
        return Bez(y1, y2, t);

        static double Bez(double a, double b, double t) =>
            3 * (1 - t) * (1 - t) * t * a + 3 * (1 - t) * t * t * b + t * t * t;
        static double BezD(double a, double b, double t) =>
            3 * (1 - t) * (1 - t) * a + 6 * (1 - t) * t * (b - a) + 3 * t * t * (1 - b);
    }

    // ----- messages système -----

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_DPICHANGED:
                // Changement d'écran : on recalcule nous-mêmes la géométrie (au DPI de l'écran
                // d'ancrage) au lieu de laisser WinForms redimensionner la fenêtre.
                if (_geometryReady && !_animating) LayoutGeometry();
                m.Result = 0;
                return;

            case WM_SETTINGCHANGE:
                // Zone de travail changée (cadre réservé, barre des tâches déplacée…).
                if (m.WParam == SPI_SETWORKAREA && _geometryReady && !_animating) LayoutGeometry();
                bool now = TransparencyEnabled();
                if (now != _blurAvailable)
                {
                    _blurAvailable = now;
                    if (_glass is not null) _glass.Blur = now;
                    ApplyBlurClass();
                }
                break;
        }
        base.WndProc(ref m);
    }

    private void ApplyBlurClass()
    {
        var core = _web.CoreWebView2;
        if (core is null) return;
        string js = _blurAvailable
            ? "document.documentElement.classList.remove('no-blur')"
            : "document.documentElement.classList.add('no-blur')";
        _ = core.ExecuteScriptAsync(js);
    }

    internal static bool TransparencyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int v || v != 0;
        }
        catch { return true; }
    }
}

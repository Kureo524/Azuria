using System.Diagnostics;
using System.Runtime.InteropServices;
using Azuria.Shell.Native;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace Azuria.Shell;

/// <summary>
/// Fenêtre de verre d'Azuria : hôte natif sans cadre, toujours au-dessus, hors barre des tâches,
/// avec l'Acrylic de DWM, qui affiche une page de l'UI (HTML/CSS) dans un WebView2 transparent.
///
/// Les panneaux sont des « tiroirs » attachés à un bord d'écran (organisation inspirée de Caelestia,
/// entièrement réécrite ici) : la fenêtre est découpée à la forme du tiroir — congés concaves là où
/// il rejoint le bord, coins arrondis côté libre — et la page dessine la même forme (web/drawer.js).
/// Un tiroir se révèle en glissant depuis son bord (seule la position bouge, cadencée par DWM),
/// soit au survol du bord (sondage du curseur), soit sur commande (<see cref="Reveal"/>).
/// </summary>
internal class GlassWindow : Form
{
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WM_DPICHANGED = 0x02E0;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private readonly string _page;
    private readonly WebView2 _web = new();
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
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(POINT pt, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint hmon, int type, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    /// <summary>Message envoyé par la page (<c>chrome.webview.postMessage</c>), sauf « close » qui range le tiroir.</summary>
    public event Action<string>? MessageReceived;

    protected GlassWindow(string page)
    {
        _page = page;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None; // on gère les tailles en pixels physiques
        BackColor = Color.Black;            // noir = verre, dans le cadre étendu par DWM

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
    protected virtual Screen AnchorScreen => Screen.PrimaryScreen ?? Screen.AllScreens[0];

    /// <summary>Forme de tiroir (null = rectangle aux coins arrondis par Windows).</summary>
    protected virtual DrawerEdge? Shape => null;

    /// <summary>Rayon des coins et des congés du tiroir, en px logiques.</summary>
    protected virtual int ShapeRadiusLogical => 14;

    /// <summary>Ne vole pas le focus (vrai pour les barres/panneaux, faux pour le lanceur où l'on tape).</summary>
    protected virtual bool NoActivate => true;

    /// <summary>Révélé au survol de son bord (faux pour un tiroir ouvert sur commande).</summary>
    protected virtual bool HoverDriven => true;

    /// <summary>Appelé quand la fenêtre doit (re)calculer sa géométrie (affichage, DPI, écran).</summary>
    protected virtual void LayoutGeometry() { }

    /// <summary>Appelé juste avant une révélation au survol, avec la position du curseur.</summary>
    protected virtual void PrepareReveal(Point cursor) { }

    protected bool IsShown => _shownState;

    protected Rectangle WorkArea => AnchorScreen.WorkingArea;

    /// <summary>
    /// DPI de l'écran d'ancrage, lu sur le moniteur (pas sur la fenêtre) : fiable même quand la
    /// fenêtre est masquée ou pas encore dessus. Nécessite PerMonitorV2 (forcé dans Program).
    /// </summary>
    protected uint TargetDpi
    {
        get
        {
            var b = AnchorScreen.Bounds;
            var pt = new POINT { X = b.Left + b.Width / 2, Y = b.Top + b.Height / 2 };
            nint mon = MonitorFromPoint(pt, 2 /* MONITOR_DEFAULTTONEAREST */);
            return GetDpiForMonitor(mon, 0 /* MDT_EFFECTIVE_DPI */, out uint d, out _) == 0 && d > 0 ? d : 96;
        }
    }

    /// <summary>Convertit une taille logique (px à 100 %) en pixels physiques pour l'écran d'ancrage.</summary>
    protected int Scale(int logical) => (int)Math.Round(logical * TargetDpi / 96.0);

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
        if (_geometryReady && !_animating && !_shownState) Slide(true);
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
            if (NoActivate) cp.ExStyle |= WS_EX_NOACTIVATE; // ne vole pas le focus
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => NoActivate;

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
        if (IsHandleCreated && Shape is DrawerEdge edge)
            Shaping.ApplyDrawer(Handle, edge, Width, Height, Scale(ShapeRadiusLogical));
    }

    protected override async void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        Dwm.UseDarkMode(Handle);
        Dwm.ExtendFrameFull(Handle);
        // Un tiroir a sa propre forme (région) : pas d'arrondi Windows par-dessus.
        Dwm.SetCorner(Handle, Shape is null ? Dwm.Corner.Round : Dwm.Corner.DoNotRound);
        _blurAvailable = TransparencyEnabled();
        Dwm.SetAcrylic(Handle, _blurAvailable);
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
        // La page reçoit la forme du tiroir pour la dessiner à l'identique (drawer.js).
        string query = Shape is DrawerEdge edge
            ? $"?edge={edge.ToString().ToLowerInvariant()}&r={ShapeRadiusLogical}"
            : "";
        core.Navigate($"https://azuria.local/{_page}{query}");

        if (HoverDriven) _hover.Start();
    }

    // ----- révélation par glissement -----

    private void OnHoverTick(object? sender, EventArgs e)
    {
        if (!_geometryReady || _animating) return;
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
            if (!shown) Visible = false;
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
                bool now = TransparencyEnabled();
                if (now != _blurAvailable)
                {
                    _blurAvailable = now;
                    Dwm.SetAcrylic(Handle, now);
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

    private static bool TransparencyEnabled()
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

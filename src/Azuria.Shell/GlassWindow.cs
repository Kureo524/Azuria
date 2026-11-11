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
/// Coins arrondis par le compositeur (DWMWA_WINDOW_CORNER_PREFERENCE). La fenêtre se révèle en
/// GLISSANT depuis un bord (comme Caelestia) : on anime seulement sa POSITION, jamais sa taille,
/// donc le contenu ne bouge pas et ne « stutter » pas. Le curseur est sondé côté C# : une zone
/// déclenche la révélation, une autre (plus large) la maintient, ce qui évite toute oscillation.
/// </summary>
internal class GlassWindow : Form
{
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly string _page;
    private readonly WebView2 _web = new();
    private bool _blurAvailable = true;

    // Géométrie (pixels physiques), fixée par la classe dérivée.
    private Rectangle _hidden;     // position cachée (hors écran)
    private Rectangle _shown;      // position révélée
    private Rectangle _triggerZone; // curseur ici (caché) → on révèle
    private Rectangle _keepZone;    // curseur ici (révélé) → on reste ; sinon on cache
    private bool _shownState;
    private bool _geometryReady;

    // Animation de position
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };
    private readonly Stopwatch _clock = new();
    private Rectangle _animFrom, _animTo;
    private double _animDurationMs;
    private bool _animDecelerate;

    // Survol (sondage du curseur) et stabilisation du DPI après affichage
    private readonly System.Windows.Forms.Timer _hover = new() { Interval = 30 };
    private readonly System.Windows.Forms.Timer _settle = new() { Interval = 50 };
    private int _settleTicks;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);

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

        _anim.Tick += OnAnimTick;
        _hover.Tick += OnHoverTick;
        _settle.Tick += (_, _) =>
        {
            LayoutGeometry();
            if (++_settleTicks >= 6) { _settle.Stop(); _settleTicks = 0; }
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        LayoutGeometry();
        _settle.Start(); // le DPI de la fenêtre n'est fiable qu'après son placement réel
    }

    /// <summary>DPI réel de la fenêtre (WinForms.DeviceDpi reste parfois bloqué à 96).</summary>
    protected uint MonitorDpi => IsHandleCreated && GetDpiForWindow(Handle) is var d and > 0 ? d : 96;

    /// <summary>Convertit une taille logique (px à 100 %) en pixels physiques pour cet écran.</summary>
    protected int Scale(int logical) => (int)Math.Round(logical * MonitorDpi / 96.0);

    /// <summary>Appelé quand la fenêtre doit (re)calculer sa géométrie (affichage, DPI, écran).</summary>
    protected virtual void LayoutGeometry() { }

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
        if (!_anim.Enabled) Bounds = _shownState ? shown : hidden;
    }

    // ----- cycle de vie / verre -----

    protected override async void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        Dwm.UseDarkMode(Handle);
        Dwm.ExtendFrameFull(Handle);
        Dwm.SetCorner(Handle, Dwm.Corner.Round); // arrondi natif, clippe aussi le matériau DWM
        _blurAvailable = TransparencyEnabled();
        Dwm.SetAcrylic(Handle, _blurAvailable);

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

        ApplyBlurClass();
        core.Navigate($"https://azuria.local/{_page}");

        _hover.Start(); // le survol pilote la révélation (voir OnHoverTick)
    }

    // ----- révélation par glissement : survol sondé -----

    private void OnHoverTick(object? sender, EventArgs e)
    {
        if (!_geometryReady) return;
        GetCursorPos(out POINT p);
        var cursor = new Point(p.X, p.Y);
        Rectangle hot = _shownState ? _keepZone : _triggerZone;
        bool want = hot.Contains(cursor);
        if (want != _shownState) AnimateTo(want);
    }

    private void AnimateTo(bool shown)
    {
        _shownState = shown;
        _animFrom = Bounds;
        _animTo = shown ? _shown : _hidden;
        _animDurationMs = shown ? 200 : 140;  // DA §11 : ouverture ~0,2 s, fermeture 0,14 s
        _animDecelerate = shown;               // entrée en décélération, sortie en accélération
        _clock.Restart();
        _anim.Start();
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        double p = Math.Clamp(_clock.Elapsed.TotalMilliseconds / _animDurationMs, 0, 1);
        double eased = _animDecelerate ? Bezier(0.2, 0, 0, 1, p) : Bezier(0.3, 0, 1, 1, p);

        Bounds = new Rectangle(
            Lerp(_animFrom.X, _animTo.X, eased),
            Lerp(_animFrom.Y, _animTo.Y, eased),
            _animTo.Width, _animTo.Height); // taille constante : seule la position glisse

        if (p >= 1)
        {
            _anim.Stop();
            _clock.Stop();
            Bounds = _animTo;
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

    // ----- repli quand la transparence de Windows est coupée -----

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_SETTINGCHANGE)
        {
            bool now = TransparencyEnabled();
            if (now != _blurAvailable)
            {
                _blurAvailable = now;
                Dwm.SetAcrylic(Handle, now);
                ApplyBlurClass();
            }
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

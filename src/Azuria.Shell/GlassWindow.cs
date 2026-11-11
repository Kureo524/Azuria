using System.Diagnostics;
using Azuria.Shell.Native;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace Azuria.Shell;

/// <summary>
/// Fenêtre de verre d'Azuria : hôte natif sans cadre, toujours au-dessus, hors barre des tâches,
/// avec l'Acrylic de DWM, qui affiche une page de l'UI (HTML/CSS) dans un WebView2 transparent.
///
/// La fenêtre est découpée en rectangle arrondi (région) pour que le verre ne déborde pas en coins
/// carrés. Pour l'effet « dynamic island », la fenêtre s'anime entre un état replié et un état
/// étendu ; l'UI (CSS) montre peu ou beaucoup selon l'état, transmis par la classe <c>expanded</c>.
/// </summary>
internal class GlassWindow : Form
{
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly string _page;
    private readonly WebView2 _web = new();
    private bool _blurAvailable = true;

    // Géométrie (pixels physiques) : les deux états et le rayon, fixés par la classe dérivée.
    private Rectangle _collapsed;
    private Rectangle _expanded;
    private int _radiusPx = 14;
    private bool _expandedState;

    // Animation
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };
    private readonly Stopwatch _clock = new();
    private Rectangle _animFrom, _animTo;
    private double _animDurationMs;
    private bool _animDecelerate;

    protected GlassWindow(string page)
    {
        _page = page;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        // Fond noir : dans le cadre étendu par DWM, le noir devient le matériau (verre).
        BackColor = Color.Black;

        _web.Dock = DockStyle.Fill;
        _web.DefaultBackgroundColor = Color.Transparent; // laisse passer le verre derrière l'UI
        Controls.Add(_web);

        _anim.Tick += OnAnimTick;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW; // hors Alt-Tab et hors barre des tâches
            return cp;
        }
    }

    /// <summary>Convertit une taille logique (px à 100 %) en pixels physiques pour cet écran.</summary>
    protected int Scale(int logical) => (int)Math.Round(logical * DeviceDpi / 96.0);

    /// <summary>Fixe les deux états de la fenêtre et le rayon. La fenêtre démarre repliée.</summary>
    protected void ConfigureGeometry(Rectangle collapsed, Rectangle expanded, int radiusPx)
    {
        _collapsed = collapsed;
        _expanded = expanded;
        _radiusPx = radiusPx;
        ApplyBounds(collapsed);
    }

    private void ApplyBounds(Rectangle r)
    {
        Bounds = r;
        Shaping.RoundWindow(Handle, r.Width, r.Height, _radiusPx);
    }

    // ----- cycle de vie / verre -----

    protected override async void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        Dwm.UseDarkMode(Handle);
        Dwm.ExtendFrameFull(Handle);
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

        core.WebMessageReceived += OnWebMessage;

        ApplyBlurClass();
        core.Navigate($"https://azuria.local/{_page}");
    }

    // ----- dynamic island : survol → animation de la fenêtre -----

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string msg = e.TryGetWebMessageAsString();
        if (msg == "expand") AnimateTo(expanded: true);
        else if (msg == "collapse") AnimateTo(expanded: false);
    }

    private void AnimateTo(bool expanded)
    {
        if (_expandedState == expanded && !_anim.Enabled) return;
        _expandedState = expanded;

        _animFrom = Bounds;
        _animTo = expanded ? _expanded : _collapsed;
        _animDurationMs = expanded ? 180 : 140;   // DA §11 : ouverture ~0,2 s, fermeture 0,14 s
        _animDecelerate = expanded;                // entrée en décélération, sortie en accélération
        _clock.Restart();
        _anim.Start();
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        double p = Math.Clamp(_clock.Elapsed.TotalMilliseconds / _animDurationMs, 0, 1);
        double eased = _animDecelerate ? Bezier(0.2, 0, 0, 1, p) : Bezier(0.3, 0, 1, 1, p);

        ApplyBounds(new Rectangle(
            Lerp(_animFrom.X, _animTo.X, eased),
            Lerp(_animFrom.Y, _animTo.Y, eased),
            Lerp(_animFrom.Width, _animTo.Width, eased),
            Lerp(_animFrom.Height, _animTo.Height, eased)));

        if (p >= 1)
        {
            _anim.Stop();
            _clock.Stop();
            ApplyBounds(_animTo);
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

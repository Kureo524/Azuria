using System.Runtime.InteropServices;
using System.Text.Json;
using Azuria.Shell.Native;
using Windows.Devices.Radios;

namespace Azuria.Shell;

/// <summary>
/// Popout de la barre (idée de Caelestia, réécrite ici) : un petit tiroir qui sort du flanc droit de
/// la barre, à hauteur de l'icône survolée, raccordé à elle par deux congés concaves. Il montre et
/// règle sur place ce que l'icône résume : réseau (Wi-Fi, Bluetooth), son (volume, sortie audio),
/// batterie, ou une appli ouverte (titre et aperçu en direct).
/// </summary>
internal sealed class PopoutWindow : GlassWindow
{
    private const int BodyWLogical = 280;
    private const int RadiusLogical = 14;
    private const int RiseLogical = 10;   // glisse de 10 depuis la barre

    private Screen _screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
    private int _x, _centerY;
    private int _bodyHLogical = 96;

    public PopoutWindow() : base("popout.html") { }

    public override Screen AnchorScreen => _screen;
    protected override bool HoverDriven => false;
    protected override Padding ContentInsets => new(0, ShellMetrics.Fillet, 0, ShellMetrics.Fillet);

    /// <summary>Hauteur du corps (sans les congés), en px logiques, d'après la page.</summary>
    public int BodyHeightLogical
    {
        get => _bodyHLogical;
        set { if (value != _bodyHLogical) { _bodyHLogical = value; LayoutGeometry(); } }
    }

    /// <summary>Haut du corps (sous le congé du haut), en pixels écran.</summary>
    public int BodyTop => Top + Scale(ShellMetrics.Fillet);

    protected override void BuildShape(GlassShape s, int w, int h)
    {
        int f = Scale(ShellMetrics.Fillet);
        s.Body(0, f, w, h - 2 * f, Scale(RadiusLogical), false, true, true, false)
         .Concave(0, 0, f, f, 0)            // raccord au flanc de la barre, en haut
         .Concave(0, h - f, f, f, h);       // et en bas
    }

    /// <summary>Place le popout contre la barre (<paramref name="x"/> = flanc droit), centré sur <paramref name="centerY"/>.</summary>
    public void Place(Screen screen, int x, int centerY)
    {
        _screen = screen;
        _x = x;
        _centerY = centerY;
        LayoutGeometry();
    }

    protected override void LayoutGeometry()
    {
        if (!IsHandleCreated || _x == 0) return;
        var work = WorkArea;
        int f = Scale(ShellMetrics.Fillet);
        int w = Scale(BodyWLogical);
        int h = Scale(_bodyHLogical) + 2 * f;
        int y = Math.Clamp(_centerY - h / 2, work.Top - f, work.Bottom - h + f);
        var shown = new Rectangle(_x, y, w, h);
        var hidden = shown with { X = _x - Scale(RiseLogical) };
        ConfigureReveal(hidden, shown, Rectangle.Empty, Rectangle.Empty);
    }
}

/// <summary>
/// Pilote le popout : suit l'icône survolée dans la barre, garde le popout tant que le curseur est
/// dessus ou sur l'icône, le range peu après sinon. Alimente son contenu et applique ses réglages.
/// </summary>
internal sealed class BarPopout : IDisposable
{
    private readonly GlassWindow _bar;
    private readonly PopoutWindow _pop;
    private readonly OpenWindows _apps;
    private readonly int _barBodyLogical;
    private readonly Radios _radios = new();
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 40 };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1000 };

    private (string Kind, string Id, double Top, double Bottom)? _hover; // icône sous le curseur (px CSS de la barre)
    private string _key = "";        // contenu affiché
    private long _lostAt;
    private nint _thumb;
    private nint _thumbSource;

    public PopoutWindow Window => _pop;

    /// <summary>Un réglage vient d'être changé depuis la barre (l'OSD n'a pas à le répéter).</summary>
    public event Action? AdjustedVolume;

    public BarPopout(GlassWindow bar, int barBodyLogical, OpenWindows apps)
    {
        _bar = bar;
        _barBodyLogical = barBodyLogical;
        _apps = apps;
        _pop = new PopoutWindow();
        _bar.KeepAlso = p => _pop.Visible && Rectangle.Inflate(_pop.Bounds, 4, 4).Contains(p);
        _bar.MessageReceived += OnBarMessage;
        _pop.MessageReceived += OnPopMessage;
        _radios.Changed += () => { if (_pop.Visible && _key == "net") SendContent(); };
        _radios.Start();
        _tick.Tick += (_, _) => Follow();
        _refresh.Tick += (_, _) => { if (_pop.Visible && _key is "net" or "snd" or "bat") SendContent(); };
        _tick.Start();
        _refresh.Start();
    }

    private void OnBarMessage(string msg)
    {
        if (!msg.StartsWith("hover:")) return;
        // « hover:<genre>:<id>:<haut>:<bas> » ou « hover: » (rien de survolable).
        var p = msg[6..].Split(':');
        if (p.Length >= 4 && double.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double top)
                          && double.TryParse(p[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double bottom))
            _hover = (p[0], p[1], top, bottom);
        else _hover = null;
    }

    private void Follow()
    {
        if (!_bar.IsShown || GlassWindow.Suspended) { Hide(); return; }
        var cursor = Cursor.Position;
        if (_pop.Visible && Rectangle.Inflate(_pop.Bounds, 2, 2).Contains(cursor)) { _lostAt = 0; return; }
        if (_hover is { } h)
        {
            _lostAt = 0;
            string key = h.Kind == "app" ? "app:" + h.Id : h.Kind;
            if (key != _key || !_pop.IsShown) Show(h, key);
            return;
        }
        if (!_pop.IsShown) return;
        long now = Environment.TickCount64;
        if (_lostAt == 0) _lostAt = now;
        else if (now - _lostAt > 180) Hide();
    }

    private void Show((string Kind, string Id, double Top, double Bottom) h, string key)
    {
        if (_pop.IsAnimating) return; // on réessaie au tour suivant
        _key = key;
        var screen = _bar.AnchorScreen;
        double scale = Monitors.Dpi(screen) / 96.0;
        int x = _bar.Left + Monitors.Scale(screen, _barBodyLogical);
        int centerY = _bar.Top + (int)Math.Round((h.Top + h.Bottom) / 2 * scale);
        _pop.Place(screen, x, centerY);
        SendContent();
        if (!_pop.IsShown) _pop.Reveal();
        UpdateThumbnail();
    }

    private void Hide()
    {
        _lostAt = 0;
        _hover = null;
        if (_pop.IsShown && !_pop.IsAnimating) _pop.Conceal();
        if (!_pop.IsShown) { _key = ""; DropThumbnail(); }
    }

    // ----- contenu -----

    private void SendContent()
    {
        object? body = null;
        switch (_key)
        {
            case "net":
                var net = Network.Current();
                body = new
                {
                    type = "net", kind = net.Kind, name = net.Name, signal = net.Signal,
                    wifi = _radios.State(RadioKind.WiFi), bluetooth = _radios.State(RadioKind.Bluetooth),
                };
                break;
            case "snd":
                var o = Audio.Output;
                body = new
                {
                    type = "snd", volume = o?.Level, muted = o?.Muted ?? false,
                    outputs = Audio.Outputs().Select(d => new { id = d.Id, name = d.Name, active = d.Default }),
                };
                break;
            case "bat":
                var (pct, charging) = SystemStats.Battery();
                body = new { type = "bat", percent = pct, charging };
                break;
            default:
                if (_key.StartsWith("app:") && long.TryParse(_key[4..], out long hw))
                {
                    var w = _apps.List().FirstOrDefault(a => a.Handle == (nint)hw);
                    if (w is null) { Hide(); return; }
                    body = new { type = "app", title = w.Title, icon = w.Icon, thumb = ThumbHeightCss((nint)hw) };
                }
                break;
        }
        if (body is not null) _pop.PostJson(JsonSerializer.Serialize(body));
    }

    private void OnPopMessage(string msg)
    {
        string[] p = msg.Split(':', 2);
        switch (p[0])
        {
            case "height":
                if (double.TryParse(p.ElementAtOrDefault(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double hh))
                {
                    _pop.BodyHeightLogical = (int)Math.Ceiling(hh);
                    UpdateThumbnail();
                }
                return;
            case "volume":
                if (int.TryParse(p.ElementAtOrDefault(1), out int v)) { Audio.SetOutputLevel(v); AdjustedVolume?.Invoke(); }
                return;
            case "mute":
                if (Audio.Output is { } o) Audio.SetOutputMuted(!o.Muted);
                AdjustedVolume?.Invoke();
                SendContent();
                return;
            case "output":
                if (p.ElementAtOrDefault(1) is { Length: > 0 } id) { Audio.SetDefaultOutput(id); AdjustedVolume?.Invoke(); }
                SendContent();
                return;
            case "toggle":
                if (p.ElementAtOrDefault(1) == "wifi") _radios.Toggle(RadioKind.WiFi);
                else if (p.ElementAtOrDefault(1) == "bluetooth") _radios.Toggle(RadioKind.Bluetooth);
                return;
            case "activate":
                if (_key.StartsWith("app:") && long.TryParse(_key[4..], out long hw)) { _apps.Activate((nint)hw); Hide(); }
                return;
            case "ready": SendContent(); return;
        }
    }

    // ----- aperçu en direct d'une appli (miniature DWM : DWM recopie l'image de la fenêtre dans la
    // nôtre ; rien n'est lu ni injecté dans l'appli). Jamais pour un jeu : la barre n'en liste pas
    // pendant une partie, et le popout se range en plein écran. -----

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_THUMBNAIL_PROPERTIES
    {
        public uint dwFlags; public RECT rcDestination; public RECT rcSource; public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }
    private const uint DWM_TNP_RECTDESTINATION = 0x1, DWM_TNP_OPACITY = 0x4, DWM_TNP_VISIBLE = 0x8, DWM_TNP_SOURCECLIENTAREAONLY = 0x10;
    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(nint dest, nint src, out nint thumb);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(nint thumb);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(nint thumb, ref DWM_THUMBNAIL_PROPERTIES props);
    [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(nint thumb, out SIZE size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint h, out RECT r);

    private const int ThumbWidthCss = 256;   // largeur de l'aperçu (px CSS), cf. popout.html
    private const int ThumbTopCss = 50;      // marge 12 + en-tête 28 + écart 10 (popout.html)
    private const int ThumbSideCss = 12;

    // Hauteur de l'aperçu en px CSS, d'après les proportions de la fenêtre.
    private static int ThumbHeightCss(nint h)
    {
        if (!GetWindowRect(h, out var r) || r.Right <= r.Left) return 140;
        double ratio = (double)(r.Bottom - r.Top) / (r.Right - r.Left);
        return Math.Clamp((int)Math.Round(ThumbWidthCss * ratio), 60, 200);
    }

    private void UpdateThumbnail()
    {
        if (!_key.StartsWith("app:") || !long.TryParse(_key[4..], out long hw)) { DropThumbnail(); return; }
        nint src = (nint)hw;
        if (src != _thumbSource) DropThumbnail();
        if (_thumb == 0)
        {
            if (DwmRegisterThumbnail(_pop.Handle, src, out _thumb) != 0) { _thumb = 0; return; }
            _thumbSource = src;
        }
        double scale = Monitors.Dpi(_pop.AnchorScreen) / 96.0;
        int f = Monitors.Scale(_pop.AnchorScreen, ShellMetrics.Fillet);
        int left = (int)Math.Round(ThumbSideCss * scale);
        int top = f + (int)Math.Round(ThumbTopCss * scale);
        int w = (int)Math.Round(ThumbWidthCss * scale);
        int h = (int)Math.Round(ThumbHeightCss(src) * scale);
        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_OPACITY | DWM_TNP_VISIBLE | DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new RECT { Left = left, Top = top, Right = left + w, Bottom = top + h },
            opacity = 255, fVisible = true, fSourceClientAreaOnly = false,
        };
        DwmUpdateThumbnailProperties(_thumb, ref props);
    }

    private void DropThumbnail()
    {
        if (_thumb != 0) DwmUnregisterThumbnail(_thumb);
        _thumb = 0;
        _thumbSource = 0;
    }

    public void Dispose()
    {
        _tick.Dispose();
        _refresh.Dispose();
        DropThumbnail();
    }
}

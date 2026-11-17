using System.Text.Json;
using Azuria.Shell.Native;

namespace Azuria.Shell;

/// <summary>
/// Petit tiroir collé au bord droit de l'espace des écrans, centré en hauteur (idée de Caelestia,
/// réécrite) : il sort du cadre en glissant, raccordé à lui par deux congés concaves, coins
/// arrondis côté libre. Sert au menu de session et à l'OSD du volume.
/// </summary>
internal abstract class RightTabWindow : GlassWindow
{
    private const int RadiusLogical = 18;
    private readonly int _bodyWLogical;
    private int _bodyHLogical;

    protected RightTabWindow(string page, int bodyWLogical, int bodyHLogical) : base(page)
    {
        _bodyWLogical = bodyWLogical;
        _bodyHLogical = bodyHLogical;
    }

    public override Screen AnchorScreen => Screen.AllScreens.OrderByDescending(s => s.Bounds.Right).First();
    public override DrawerEdge Edge => DrawerEdge.Right;
    public override DrawerSides AttachedSides => DrawerSides.Right;
    protected override bool HoverDriven => false;
    protected override Padding ContentInsets => new(0, ShellMetrics.Fillet, 0, ShellMetrics.Fillet);

    /// <summary>Hauteur du corps en px logiques (la page peut l'ajuster).</summary>
    protected int BodyHeightLogical
    {
        get => _bodyHLogical;
        set { if (value != _bodyHLogical) { _bodyHLogical = value; LayoutGeometry(); } }
    }

    protected override void BuildShape(GlassShape s, int w, int h)
    {
        int f = Scale(ShellMetrics.Fillet);
        s.Body(0, f, w, h - 2 * f, Scale(RadiusLogical), true, false, false, true)
         .Concave(w - f, 0, f, w - f, 0)    // raccord au bord droit du cadre, en haut
         .Concave(w - f, h - f, f, w - f, h);
    }

    protected override void LayoutGeometry()
    {
        if (!IsHandleCreated) return;
        var work = WorkArea;
        var screen = AnchorScreen.Bounds;
        int f = Scale(ShellMetrics.Fillet);
        int w = Scale(_bodyWLogical);
        int h = Scale(_bodyHLogical) + 2 * f;
        int y = work.Top + (work.Height - h) / 2;
        var shown = new Rectangle(work.Right - w, y, w, h);
        var hidden = shown with { X = screen.Right };
        ConfigureReveal(hidden, shown, Rectangle.Empty, Rectangle.Empty);
    }

    protected void Post(object o) => PostJson(JsonSerializer.Serialize(o));
}

/// <summary>
/// Menu de session (DA §9) : verrouiller, veille, déconnexion, redémarrage, arrêt, quitter Azuria,
/// en boutons empilés. S'ouvre par le bouton d'alimentation de la barre ; se range sur Échap ou au
/// clic ailleurs. Ce qui ferme la session demande un second clic.
/// </summary>
internal sealed class SessionWindow : RightTabWindow
{
    private readonly Action<string> _run;

    public SessionWindow(Action<string> run) : base("session.html", 88, 6 * 62 + 2 * 12)
    {
        _run = run;
        MessageReceived += msg =>
        {
            if (!msg.StartsWith("session:")) return;
            Conceal();
            _run(msg[8..]);
        };
    }

    protected override bool NoActivate => false; // Échap

    public void Toggle()
    {
        if (IsShown) Conceal();
        else { LayoutGeometry(); Reveal(); }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Conceal();
    }
}

/// <summary>
/// OSD du volume (idée de Caelestia) : quand le volume change (touches du clavier, une appli…), une
/// languette sort du bord droit avec le niveau, puis se range après un instant. Survolée, elle reste
/// et se règle (glisser, molette, clic sur l'icône pour couper).
/// </summary>
internal sealed class OsdWindow : RightTabWindow
{
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 200 };
    private (int Level, bool Muted)? _last;
    private long _hideAt;
    private long _quietUntil;

    public OsdWindow() : base("osd.html", 52, 216)
    {
        _poll.Tick += (_, _) => Poll();
        MessageReceived += msg =>
        {
            if (msg.StartsWith("volume:") && int.TryParse(msg[7..], out int v)) { Audio.SetOutputLevel(v); Keep(); }
            else if (msg == "mute") { if (Audio.Output is { } o) Audio.SetOutputMuted(!o.Muted); Keep(); }
        };
    }

    /// <summary>Le volume vient d'être changé depuis Azuria (popout de la barre) : pas d'OSD pour ça.</summary>
    public void Quiet() => _quietUntil = Environment.TickCount64 + 1200;

    /// <summary>Autre tiroir au même endroit (session) : l'OSD s'efface.</summary>
    public Func<bool>? Blocked { get; set; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _last = Audio.Output;
        _poll.Start();
    }

    private void Keep() => _hideAt = Environment.TickCount64 + 1500;

    private void Poll()
    {
        var now = Audio.Output;
        long t = Environment.TickCount64;
        bool changed = now is { } n && _last is { } l && (n.Level != l.Level || n.Muted != l.Muted);
        _last = now;
        if (now is { } cur && (changed || IsShown)) Post(new { type = "volume", level = cur.Level, muted = cur.Muted });
        if (Suspended || Blocked?.Invoke() == true) { if (IsShown) Conceal(); return; }
        if (changed && t > _quietUntil)
        {
            Keep();
            if (!IsShown) { LayoutGeometry(); Reveal(); }
        }
        if (IsShown && Bounds.Contains(Cursor.Position)) Keep();
        if (IsShown && t > _hideAt) Conceal();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _poll.Dispose();
        base.Dispose(disposing);
    }
}
